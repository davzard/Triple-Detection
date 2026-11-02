using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TripleDetection.Application.Services;
using TripleDetection.Application.VmServices;
using TripleDetection.Domain;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Enums;
using TripleDetection.Infrastructure.Device;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 产线编排器（设计文档 §3.2/§3.3）：把 PLC 触发、检测引擎、超时看门狗、
    /// 状态机与剔废/停线动作串成无人值守闭环。
    /// 所有事件回调均在非 UI 线程执行，内部用锁串行化共享状态。
    /// </summary>
    public class LineOrchestrator : IDisposable
    {
        private readonly IPlcGateway _plc;
        private readonly ILineController _line;
        private readonly IDetectionEngine _engine;
        private readonly DetectionWatchdog _watchdog;
        private readonly SystemConfigService _config;
        private readonly LoggingService _logService;
        private readonly IDetectionRecordService _recordService;
        private readonly ITaskService _taskService;
        private readonly IProductService _productService;
        private readonly StationContext _stationContext;

        private readonly object _gate = new object();

        // 策略参数（Start 时从 SystemConfigService 读取）
        private int _captureTimeoutMs = 5000;
        private string _onCaptureTimeout = "TreatAsNG";
        private int _rejectDelayMs = 50;
        private int _rejectDurationMs = 200;

        // 当前任务上下文（开班时设置；合成超时 NG 记录用）
        private int _taskId;
        private int _productId;
        private string _batchNumber = string.Empty;
        private DateTime? _productionDate;
        private DateTime? _expirationDate;
        private bool _taskContextSet;

        // 触发-结果配对（防止迟到结果与超时双重处理）
        private int _detecting;
        private bool _cycleHandled;
        private bool _started;

        // ===== Phase 2：班次 / 节拍 / 方案崩溃自动恢复（设计文档 §3.3.4/§3.3.5）=====
        private readonly ShiftService? _shiftService;
        private readonly CycleTimeMonitor? _cycleMonitor;
        private readonly HmiSoundService? _hmiSound;
        private readonly ITimeSyncService? _timeSync;
        private int _maxReloadAttempts = 2;
        private int _reloadAttempts;                  // 连续重载次数（出现一次正常结果后清零）
        private string _solutionPath = string.Empty;  // 当前方案路径（崩溃后自动重载用）
        private ProdTask? _currentTask;               // 当前任务（崩溃后重写三效期用）

        /// <summary>
        /// M9-lite 开班前时间偏差确认（设计文档 §3.9）：偏差 > 5s 时调用，
        /// 返回 true 继续开班、false 中止。未设置时默认继续（仅记日志）。
        /// 由 UI 层挂接（弹框让操作员确认）。
        /// </summary>
        public Func<TimeSpan, bool>? TimeDriftCheck;

        public LineOrchestrator(
            IPlcGateway plc,
            ILineController line,
            IDetectionEngine engine,
            DetectionWatchdog watchdog,
            SystemConfigService config,
            LoggingService logService,
            IDetectionRecordService recordService,
            ITaskService taskService,
            IProductService productService,
            StationContext stationContext,
            ShiftService? shiftService = null,
            CycleTimeMonitor? cycleMonitor = null,
            HmiSoundService? hmiSound = null,
            ITimeSyncService? timeSync = null)
        {
            _plc = plc;
            _line = line;
            _engine = engine;
            _watchdog = watchdog;
            _config = config;
            _logService = logService;
            _recordService = recordService;
            _taskService = taskService;
            _productService = productService;
            _stationContext = stationContext;
            _shiftService = shiftService;
            _cycleMonitor = cycleMonitor;
            _hmiSound = hmiSound;
            _timeSync = timeSync;
        }

        // ===== 生命周期 =====

        /// <summary>启动编排：读取策略配置、连接 PLC、订阅事件。应用登录成功后调用一次。</summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_started) return;
                _started = true;
            }

            _captureTimeoutMs = _config.GetInt(SystemConfigService.CatDetection, SystemConfigService.KeyCaptureTimeoutMs, 5000);
            _onCaptureTimeout = _config.GetString(SystemConfigService.CatDetection, SystemConfigService.KeyOnCaptureTimeout, "TreatAsNG");
            _rejectDelayMs = _config.GetInt(SystemConfigService.CatReject, SystemConfigService.KeyRejectDelayMs, 50);
            _rejectDurationMs = _config.GetInt(SystemConfigService.CatReject, SystemConfigService.KeyRejectDurationMs, 200);
            _maxReloadAttempts = _config.GetInt(SystemConfigService.CatRecovery, SystemConfigService.KeyMaxReloadAttempts, 2);

            _plc.TriggerReceived += OnPlcTrigger;
            _plc.RecipeChanged += OnRecipeChanged;
            _plc.PlcDisconnected += OnPlcDisconnected;
            _plc.PlcReconnected += OnPlcReconnected;
            _engine.DetectionResult += OnEngineResult;
            _engine.SolutionCrashed += OnSolutionCrashed;
            _watchdog.Timeout += OnWatchdogTimeout;
            _line.StateChanged += OnLineStateChanged;
            _line.AlarmRaised += OnLineAlarm;

            try
            {
                _plc.ConnectAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logService?.LogError("产线", $"PLC 连接启动失败：{ex.Message}");
            }
            _logService?.LogInfo("产线", $"编排器已启动：超时={_captureTimeoutMs}ms 策略={_onCaptureTimeout} 剔废={_rejectDelayMs}/{_rejectDurationMs}ms");
        }

        /// <summary>停止编排：停看门狗、退订事件、断开 PLC。应用退出时调用。</summary>
        public void Stop()
        {
            bool wasStarted;
            lock (_gate)
            {
                wasStarted = _started;
                _started = false;
            }
            if (!wasStarted) return;

            _watchdog.Stop();
            _plc.TriggerReceived -= OnPlcTrigger;
            _plc.RecipeChanged -= OnRecipeChanged;
            _plc.PlcDisconnected -= OnPlcDisconnected;
            _plc.PlcReconnected -= OnPlcReconnected;
            _engine.DetectionResult -= OnEngineResult;
            _engine.SolutionCrashed -= OnSolutionCrashed;
            _watchdog.Timeout -= OnWatchdogTimeout;
            _line.StateChanged -= OnLineStateChanged;
            _line.AlarmRaised -= OnLineAlarm;

            // 退出前清除安灯（若仍有活动报警码）
            try { _plc.WriteAlarmCodeAsync(0).GetAwaiter().GetResult(); }
            catch { /* 退出路径不阻塞 */ }

            try { _plc.DisconnectAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { _logService?.LogWarn("产线", $"PLC 断开失败：{ex.Message}"); }
        }

        // ===== 班次操作（UI 调用，任意线程） =====

        /// <summary>
        /// 开班：校验任务与产品 → 设置引擎任务上下文 → 加载方案 → 写入三期 ToVM → 状态机 Idle→Ready。
        /// 返回 false 表示开班失败（任务/产品/方案无效）。
        /// </summary>
        public bool StartShift(int taskId)
        {
            // M9-lite 开班前时间同步检查（设计文档 §3.9）：偏差 > 5s 强制告警，操作员确认后才继续
            if (_timeSync != null)
            {
                var offset = _timeSync.GetOffset(TimeSpan.FromSeconds(3));
                if (offset != null && Math.Abs(offset.Value.TotalSeconds) > 5)
                {
                    bool proceed = TimeDriftCheck?.Invoke(offset.Value) ?? true;
                    _logService?.LogWarn("产线", $"开班前时间偏差 {offset.Value.TotalSeconds:F1}s（>5s）{(proceed ? "，操作员确认后继续" : "，已中止开班")}");
                    if (!proceed) return false;
                }
            }

            var task = _taskService?.GetById(taskId);
            if (task == null || task.Status != Domain.Enums.TaskStatus.Approved)
            {
                _logService?.LogWarn("产线", $"开班失败：任务 {taskId} 不存在或未审批");
                return false;
            }
            var product = _productService?.GetById(task.ProductId);
            if (product == null)
            {
                _logService?.LogWarn("产线", $"开班失败：产品 {task.ProductId} 不存在");
                return false;
            }
            if (string.IsNullOrEmpty(product.SolFilePath))
            {
                _logService?.LogWarn("产线", $"开班失败：产品 {product.Code} 未配置方案路径");
                return false;
            }

            try
            {
                _engine.LoadSolution(product.SolFilePath);
            }
            catch (Exception ex)
            {
                _logService?.LogError("产线", $"开班失败：方案加载异常 {ex.Message}");
                return false;
            }
            if (!_engine.IsSolutionLoaded)
            {
                _logService?.LogError("产线", $"开班失败：方案加载未生效（{product.SolFilePath}）");
                return false;
            }

            _engine.SetCurrentTaskContext(task.Id, task.ProductId, task.BatchNumber, task.ProductionDate, task.ExpirationDate);
            WriteThreePeriodToVm(task);

            lock (_gate)
            {
                _taskId = task.Id;
                _productId = task.ProductId;
                _batchNumber = task.BatchNumber ?? string.Empty;
                _productionDate = task.ProductionDate;
                _expirationDate = task.ExpirationDate;
                _taskContextSet = true;
                _solutionPath = product.SolFilePath;
                _currentTask = task;
                _reloadAttempts = 0;
            }
            _shiftService?.OpenShift(task.Id, SessionManager.CurrentUserId);
            _line.StartShift();
            _logService?.LogInfo("产线", $"开班成功：任务={task.Name} 产品={product.Code} 批次={task.BatchNumber}");
            return true;
        }

        public void StartAutoRun() => _line.StartAutoRun();

        public void StopAutoRun() => _line.StopAutoRun();

        public void Pause() => _line.Pause();

        public void ResetAlarm()
        {
            _line.ResetAlarm();
            _hmiSound?.Reset();
            // 安灯清除：报警复位后写 0 到 ALARM_CODE 寄存器（电气侧熄灭）
            Task.Run(async () =>
            {
                try { await _plc.WriteAlarmCodeAsync(0); }
                catch (Exception ex) { _logService?.LogWarn("产线", $"安灯清除写入失败：{ex.Message}"); }
            });
        }

        public void EndShift()
        {
            _watchdog.Stop();
            _line.EndShift();
            _hmiSound?.Reset();
            lock (_gate)
            {
                _taskContextSet = false;
                _currentTask = null;
                _solutionPath = string.Empty;
                _reloadAttempts = 0;
            }
            _shiftService?.CloseShift();
            // 下班同样清除安灯
            Task.Run(async () =>
            {
                try { await _plc.WriteAlarmCodeAsync(0); }
                catch (Exception ex) { _logService?.LogWarn("产线", $"安灯清除写入失败：{ex.Message}"); }
            });
            _logService?.LogInfo("产线", "下班：自动运行已停止");
        }

        // ===== PLC 事件 =====

        private void OnPlcTrigger(object sender, PlcTriggerEventArgs e)
        {
            if (_line.State != LineState.Running)
            {
                _logService?.LogWarn("产线", $"收到 PLC 触发但状态为 {_line.State}，忽略");
                return;
            }
            if (Interlocked.Exchange(ref _detecting, 1) == 1)
            {
                _logService?.LogWarn("产线", "上一件检测未完成，丢弃本次触发");
                return;
            }
            if (!_engine.IsSolutionLoaded || !_engine.HasProcedure)
            {
                Interlocked.Exchange(ref _detecting, 0);
                _logService?.LogError("产线", "收到触发但方案未就绪，忽略");
                return;
            }

            lock (_gate) { _cycleHandled = false; }
            _watchdog.Start(_captureTimeoutMs);
            try
            {
                _engine.RunOnce();
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _detecting, 0);
                _watchdog.Stop();
                _logService?.LogError("产线", $"RunOnce 异常：{ex.Message}");
                // RunOnce 异常且方案已卸载 → 视为崩溃（事件可能未触发），主动尝试恢复
                if (!_engine.IsSolutionLoaded)
                    TryReloadSolution();
            }
        }

        private void OnEngineResult(object sender, DetectionResult result)
        {
            _watchdog.Stop();
            Interlocked.Exchange(ref _detecting, 0);

            bool cycleHandled;
            lock (_gate)
            {
                cycleHandled = _cycleHandled;
                _cycleHandled = true;
            }
            if (cycleHandled)
            {
                _logService?.LogWarn("产线", "收到迟到的检测结果（超时已处理），忽略");
                return;
            }

            bool isTimeout = result.NgReason == NgReason.Timeout;
            _line.OnDetectionResult(result.IsOK, isTimeout);
            _hmiSound?.ReportResult(result.IsOK);

            // Phase 2：节拍监控 + 班次统计 + 崩溃恢复计数清零
            _cycleMonitor?.Record(result.ElapsedMs);
            _shiftService?.RecordResult(result.IsOK);
            if (result.IsOK)
            {
                lock (_gate) _reloadAttempts = 0;
            }

            Task.Run(async () =>
            {
                try
                {
                    await _plc.WriteResultAsync(result.IsOK ? DetectionVerdict.Ok : DetectionVerdict.Ng);
                    if (!result.IsOK)
                        await _plc.WriteRejectCommandAsync(_rejectDelayMs, _rejectDurationMs);
                }
                catch (Exception ex)
                {
                    _logService?.LogWarn("产线", $"PLC 结果写入失败：{ex.Message}");
                }
            });
        }

        private void OnPlcDisconnected(object sender, EventArgs e) => _line.OnPlcDisconnected();

        private void OnPlcReconnected(object sender, EventArgs e) => _line.OnPlcReconnected();

        private void OnRecipeChanged(object sender, RecipeChangeEventArgs e)
        {
            _logService?.LogInfo("产线", $"收到换型请求：RecipeId={e.RecipeId}");
            // 换型期间冻结触发（Running→Paused 由状态机处理，换型完成自动恢复）
            var wasRunning = _line.State == LineState.Running;
            if (wasRunning) _line.Pause();

            try
            {
                bool ok = ApplyRecipe(e.RecipeId);
                if (!ok)
                    _line.RaiseAlarm(LineAlarmCode.RecipeNotFound, $"换型失败：RecipeId={e.RecipeId} 未找到对应产品/已审批任务");
            }
            catch (Exception ex)
            {
                _line.RaiseAlarm(LineAlarmCode.RecipeNotFound, $"换型异常：{ex.Message}");
            }
            finally
            {
                if (wasRunning && _line.State == LineState.Paused)
                    _line.StartAutoRun();
            }
        }

        /// <summary>按 RECIPE_ID 匹配产品（Code 或 Id），取该产品最新已审批任务并切换上下文。</summary>
        private bool ApplyRecipe(int recipeId)
        {
            var product = _productService?.GetAll()
                .FirstOrDefault(p => p.Id == recipeId
                             || string.Equals(p.Code, recipeId.ToString(), StringComparison.Ordinal));
            if (product == null)
            {
                _logService?.LogWarn("产线", $"换型失败：找不到 Code={recipeId} 的产品");
                return false;
            }
            var task = _taskService?.GetByStatus(Domain.Enums.TaskStatus.Approved)
                .Where(t => t.ProductId == product.Id)
                .OrderByDescending(t => t.Id)
                .FirstOrDefault();
            if (task == null)
            {
                _logService?.LogWarn("产线", $"换型失败：产品 {product.Code} 无已审批任务");
                return false;
            }

            try
            {
                _engine.LoadSolution(product.SolFilePath);
            }
            catch (Exception ex)
            {
                _logService?.LogError("产线", $"换型失败：方案加载异常 {ex.Message}");
                return false;
            }
            if (!_engine.IsSolutionLoaded) return false;

            _engine.SetCurrentTaskContext(task.Id, task.ProductId, task.BatchNumber, task.ProductionDate, task.ExpirationDate);
            WriteThreePeriodToVm(task);
            lock (_gate)
            {
                _taskId = task.Id;
                _productId = task.ProductId;
                _batchNumber = task.BatchNumber ?? string.Empty;
                _productionDate = task.ProductionDate;
                _expirationDate = task.ExpirationDate;
                _taskContextSet = true;
                _solutionPath = product.SolFilePath;
                _currentTask = task;
                _reloadAttempts = 0;
            }
            _logService?.LogInfo("产线", $"换型完成：产品={product.Code} 任务={task.Name} 批次={task.BatchNumber}");
            return true;
        }

        // ===== Phase 2：方案崩溃自动恢复（设计文档 §3.3.4/§3.3.5）=====

        private void OnSolutionCrashed(object? sender, EventArgs e)
        {
            if (_line.State == LineState.Idle) return; // 未开班，无需恢复
            _logService?.LogWarn("产线", "检测到方案崩溃，尝试自动重载…");
            TryReloadSolution();
        }

        /// <summary>
        /// 自动重载当前方案（限制次数，超限触发 Alarm 2002）。
        /// 重载成功后重写任务上下文与三效期全局变量，产线保持 Running 继续自动运行。
        /// </summary>
        private void TryReloadSolution()
        {
            string solPath;
            ProdTask? task;
            int attempt;
            lock (_gate)
            {
                _reloadAttempts++;
                attempt = _reloadAttempts;
                solPath = _solutionPath;
                task = _currentTask;
            }

            if (string.IsNullOrEmpty(solPath) || task == null)
            {
                _logService?.LogError("产线", "方案崩溃：无任务上下文，无法自动重载");
                _line.RaiseAlarm(LineAlarmCode.SolutionCrash, "方案崩溃且无任务上下文，无法自动重载");
                return;
            }

            if (attempt > _maxReloadAttempts)
            {
                _logService?.LogError("产线", $"方案崩溃：自动重载已达上限（{_maxReloadAttempts} 次），触发报警");
                _line.RaiseAlarm(LineAlarmCode.SolutionCrash, $"方案崩溃：自动重载超过上限（{_maxReloadAttempts} 次），需人工处理");
                return;
            }

            try
            {
                _engine.LoadSolution(solPath);
                if (!_engine.IsSolutionLoaded)
                    throw new InvalidOperationException("方案加载未生效");
                _engine.SetCurrentTaskContext(task.Id, task.ProductId, task.BatchNumber, task.ProductionDate, task.ExpirationDate);
                WriteThreePeriodToVm(task);
                _logService?.LogInfo("产线", $"方案自动重载成功（第 {attempt}/{_maxReloadAttempts} 次），产线继续运行");
            }
            catch (Exception ex)
            {
                _logService?.LogError("产线", $"方案自动重载失败（第 {attempt}/{_maxReloadAttempts} 次）：{ex.Message}");
                if (attempt >= _maxReloadAttempts)
                    _line.RaiseAlarm(LineAlarmCode.SolutionCrash, $"方案崩溃：自动重载失败 {attempt} 次，需人工处理");
            }
        }

        // ===== 状态机事件 =====

        private void OnLineStateChanged(object sender, LineStateChangedEventArgs e)
        {
            int status = e.To == LineState.Running ? 1 : (e.To == LineState.Alarm ? 2 : 0);
            Task.Run(async () =>
            {
                try { await _plc.WriteStationStatusAsync(status); }
                catch (Exception ex) { _logService?.LogWarn("产线", $"STATION_STATUS 写入失败：{ex.Message}"); }
            });
        }

        private void OnLineAlarm(object sender, LineAlarmEventArgs e)
        {
            if (e == null) return;
            _hmiSound?.ReportAlarm();
            Task.Run(async () =>
            {
                try { await _plc.WriteLineStopAsync(e.AlarmCode); }
                catch (Exception ex) { _logService?.LogWarn("产线", $"LINE_STOP 写入失败：{ex.Message}"); }
            });
        }

        // ===== 超时看门狗 =====

        private void OnWatchdogTimeout(object sender, EventArgs e)
        {
            Interlocked.Exchange(ref _detecting, 0);

            bool cycleHandled;
            lock (_gate)
            {
                cycleHandled = _cycleHandled;
                _cycleHandled = true;
            }
            if (cycleHandled)
            {
                _logService?.LogWarn("产线", "看门狗超时（结果已处理），忽略");
                return;
            }

            _logService?.LogError("产线", $"检测超时（>{_captureTimeoutMs}ms），策略={_onCaptureTimeout}");

            if (string.Equals(_onCaptureTimeout, "StopLine", StringComparison.OrdinalIgnoreCase))
            {
                _line.RaiseAlarm(LineAlarmCode.ConsecutiveTimeout, $"检测超时（StopLine 策略，>{_captureTimeoutMs}ms）");
                return;
            }

            // 连续超时计数（TreatAsNG 与 Ignore 都计，用于发现相机/方案卡死）
            _line.OnDetectionResult(false, isTimeout: true);

            if (string.Equals(_onCaptureTimeout, "Ignore", StringComparison.OrdinalIgnoreCase))
            {
                _logService?.LogWarn("产线", "超时策略 Ignore：不记 NG、不剔废");
                return;
            }

            // TreatAsNG（默认，安全侧）：合成 Timeout NG 走正常入库/剔废路径
            _hmiSound?.ReportResult(false);
            SaveTimeoutNgRecord();
            Task.Run(async () =>
            {
                try
                {
                    await _plc.WriteResultAsync(DetectionVerdict.Ng);
                    await _plc.WriteRejectCommandAsync(_rejectDelayMs, _rejectDurationMs);
                }
                catch (Exception ex)
                {
                    _logService?.LogWarn("产线", $"超时 NG 写入 PLC 失败：{ex.Message}");
                }
            });
        }

        /// <summary>合成超时 NG 检测记录（与引擎正常入库路径一致的字段）。</summary>
        private void SaveTimeoutNgRecord()
        {
            int taskId; int productId; string batch;
            DateTime? mfg; DateTime? exp; bool hasContext;
            lock (_gate)
            {
                taskId = _taskId;
                productId = _productId;
                batch = _batchNumber;
                mfg = _productionDate;
                exp = _expirationDate;
                hasContext = _taskContextSet;
            }
            if (!hasContext)
            {
                _logService?.LogWarn("产线", "超时 NG：无任务上下文，跳过入库");
                return;
            }
            try
            {
                var record = new DetectionRecord
                {
                    TaskId = taskId,
                    ProductId = productId,
                    BatchNumber = batch,
                    IsOK = false,
                    ProductionDate = (mfg ?? DateTime.Now).ToString("yyyy-MM-dd"),
                    ExpirationDate = (exp ?? DateTime.Now.AddYears(1)).ToString("yyyy-MM-dd"),
                    ImagePath = string.Empty,
                    ElapsedMs = _captureTimeoutMs,
                    DetectionTime = DateTime.Now,
                    NgReason = NgReason.Timeout,
                    NgDetail = $"检测超时（>{_captureTimeoutMs}ms）",
                    Confidence = null,
                    CameraId = "CAM1",
                    OperatorId = SessionManager.CurrentUserId,
                    StationCode = _stationContext?.GetStationCode() ?? "UNKNOWN",
                    SequenceNo = _stationContext?.NextSequence() ?? 0
                };
                _recordService?.Save(record);
                _logService?.LogWarn("产线", $"超时 NG 记录已入库：批次={batch}");
            }
            catch (Exception ex)
            {
                _logService?.LogError("产线", $"超时 NG 记录入库失败：{ex.Message}");
            }
        }

        // ===== 辅助 =====

        /// <summary>写入三期信息到 VM 全局变量（BN/Mfg/EXP，格式与 DetectionView 一致）。</summary>
        private void WriteThreePeriodToVm(ProdTask task)
        {
            try
            {
                var batch = task.BatchNumber ?? string.Empty;
                var mfg = task.ProductionDate.ToString("yyyyMMdd");
                var exp = task.ExpirationDate.HasValue ? task.ExpirationDate.Value.ToString("yyyyMMdd") : string.Empty;
                _engine.SetGlobalVariableString("BN", batch);
                _engine.SetGlobalVariableString("Mfg", mfg);
                _engine.SetGlobalVariableString("EXP", exp);
                _logService?.LogInfo("产线", $"三期 ToVM：BN={batch} Mfg={mfg} EXP={exp}");
            }
            catch (Exception ex)
            {
                _logService?.LogWarn("产线", $"三期 ToVM 失败：{ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _watchdog.Dispose();
        }
    }
}
