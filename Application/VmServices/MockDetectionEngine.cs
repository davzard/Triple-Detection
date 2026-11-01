using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TripleDetection.Application.Services;
using TripleDetection.Domain.Entities;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.VmServices
{
    /// <summary>
    /// 模拟检测引擎：在没有 VisionMaster SDK / 相机 / 方案文件时，
    /// 模拟"加载方案 → 运行 → 回调 → 记录入库"链路，
    /// 便于在无硬件环境下开发与演示。
    /// 结果回调在后台线程触发，与真实 SDK 的回调线程模型一致。
    /// </summary>
    public class MockDetectionEngine : IDetectionEngine
    {
        private const double OkProbability = 0.85;
        private const int ContinuousIntervalMs = 2000;

        private readonly LoggingService _logService;
        private readonly IDetectionRecordService _detectionRecordService;
        private readonly Random _random = new Random();
        private readonly object _lock = new object();

        private Timer _continuousTimer;
        private bool _isSolutionLoaded;
        private string _currentProcedureName = string.Empty;

        private int _currentTaskId;
        private int _currentProductId;
        private string _currentBatchNumber = string.Empty;
        private DateTime? _currentProductionDate;
        private DateTime? _currentExpirationDate;

        private readonly Dictionary<string, string> _globalVariables = new Dictionary<string, string>();

        public event EventHandler<DetectionResult> DetectionResult;

        public bool IsSolutionLoaded => _isSolutionLoaded;
        public bool HasProcedure => _isSolutionLoaded && !string.IsNullOrEmpty(_currentProcedureName);
        public bool IsContinuousRun => _continuousTimer != null;

        public MockDetectionEngine(LoggingService logService, IDetectionRecordService detectionRecordService)
        {
            _logService = logService;
            _detectionRecordService = detectionRecordService;
        }

        public void LoadSolution(string solPath)
        {
            _isSolutionLoaded = true;
            _currentProcedureName = "主检测流程";
            _logService?.Log($"[Mock] 已加载方案（模拟模式，不要求文件存在）: {solPath}");
        }

        public List<string> GetAllProcedureNames()
        {
            return _isSolutionLoaded
                ? new List<string> { "主检测流程" }
                : new List<string>();
        }

        public void SetProcedure(string procedureName)
        {
            if (_isSolutionLoaded && !string.IsNullOrEmpty(procedureName))
                _currentProcedureName = procedureName;
        }

        public void SetCurrentTaskContext(int taskId, int productId, string batchNumber, DateTime? productionDate, DateTime? expirationDate)
        {
            _currentTaskId = taskId;
            _currentProductId = productId;
            _currentBatchNumber = batchNumber ?? string.Empty;
            _currentProductionDate = productionDate;
            _currentExpirationDate = expirationDate;
        }

        public void RunOnce()
        {
            if (!_isSolutionLoaded)
            {
                _logService?.Log("[Mock] 请先加载方案");
                return;
            }

            var sw = Stopwatch.StartNew();
            // 在后台线程模拟检测耗时与 SDK 回调线程模型
            Task.Run(() =>
            {
                try
                {
                    Thread.Sleep(_random.Next(120, 450));

                    bool isOK = _random.NextDouble() < OkProbability;
                    string batch = string.IsNullOrEmpty(_currentBatchNumber)
                        ? $"MOCK{DateTime.Now:yyyyMMddHHmmssfff}"
                        : _currentBatchNumber;
                    string mfg = (_currentProductionDate ?? DateTime.Now).ToString("yyyy-MM-dd");
                    string exp = (_currentExpirationDate ?? DateTime.Now.AddYears(1)).ToString("yyyy-MM-dd");

                    sw.Stop();

                    var result = new DetectionResult
                    {
                        IsOK = isOK,
                        BatchNumber = batch,
                        ProductionDate = mfg,
                        ExpirationDate = exp,
                        ImagePath = string.Empty,
                        DetectionTime = DateTime.Now,
                        ElapsedMs = sw.ElapsedMilliseconds
                    };

                    // 保存 DetectionRecord（与 VmIntegrationService 一致，非阻塞，异常吞噬）
                    try
                    {
                        var record = new DetectionRecord
                        {
                            TaskId = _currentTaskId,
                            ProductId = _currentProductId,
                            BatchNumber = batch,
                            IsOK = isOK,
                            ProductionDate = mfg,
                            ExpirationDate = exp,
                            ImagePath = string.Empty,
                            ElapsedMs = sw.ElapsedMilliseconds,
                            DetectionTime = DateTime.Now
                        };
                        _detectionRecordService?.Save(record);
                    }
                    catch (Exception ex)
                    {
                        _logService?.Log($"[Mock] DetectionRecord 保存失败: {ex.Message}");
                    }

                    _logService?.Log($"[Mock] 检测完成: {(isOK ? "OK" : "NG")} | 批号={batch} | 耗时={sw.ElapsedMilliseconds}ms");
                    DetectionResult?.Invoke(this, result);
                }
                catch (Exception ex)
                {
                    _logService?.Log($"[Mock] 检测异常: {ex.Message}");
                }
            });
        }

        public void SetContinuousRun(bool enable)
        {
            if (enable)
            {
                if (_continuousTimer == null && _isSolutionLoaded)
                {
                    _continuousTimer = new Timer(_ => RunOnce(), null, 0, ContinuousIntervalMs);
                    _logService?.Log($"[Mock] 连续运行已启动（每 {ContinuousIntervalMs / 1000} 秒一次）");
                }
            }
            else
            {
                if (_continuousTimer != null)
                {
                    _continuousTimer.Dispose();
                    _continuousTimer = null;
                    _logService?.Log("[Mock] 连续运行已停止");
                }
            }
        }

        public void Stop()
        {
            SetContinuousRun(false);
        }

        public void SetGlobalVariableString(string name, string value)
        {
            lock (_lock)
            {
                _globalVariables[name] = value ?? string.Empty;
            }
            _logService?.Log($"[Mock GlobalVariable] {name} = '{value}'");
        }

        public string GetGlobalVariableString(string name)
        {
            lock (_lock)
            {
                return _globalVariables.TryGetValue(name, out var v) ? v : null;
            }
        }
    }
}
