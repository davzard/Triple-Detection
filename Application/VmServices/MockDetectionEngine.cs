using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using TripleDetection.Application.Line;
using TripleDetection.Application.Services;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Enums;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.VmServices
{
    /// <summary>
    /// 模拟检测引擎：在没有 VisionMaster SDK / 相机 / 方案文件时，
    /// 模拟完整的"加载方案 → 运行 → 回调解析 → 图像归档 → 记录入库"链路，
    /// 便于在无硬件环境下开发与演示。
    /// 结果回调在后台线程触发，与真实 SDK 的回调线程模型一致。
    /// </summary>
    public class MockDetectionEngine : IDetectionEngine, ICameraHeartbeat
    {
        private const double OkProbability = 0.85;
        private const int ContinuousIntervalMs = 2000;
        private const int HeartbeatIntervalMs = 1000;

        private static readonly NgReason[] NgReasonPool =
        {
            NgReason.BnMismatch, NgReason.MfgMismatch, NgReason.ExpMismatch,
            NgReason.BnMissing, NgReason.MfgMissing, NgReason.ExpMissing, NgReason.OcrFailed
        };

        private readonly LoggingService _logService;
        private readonly IDetectionRecordService _detectionRecordService;
        private readonly ImageArchiver _imageArchiver;
        private readonly StationContext _stationContext;
        private readonly Random _random = new Random();
        private readonly object _lock = new object();

        private Timer _continuousTimer;
        private Timer _heartbeatTimer;
        private volatile bool _isSolutionLoaded;
        private volatile bool _cameraAlive = true;
        private DateTime? _lastFrameTime;
        private string _currentProcedureName = string.Empty;

        private int _currentTaskId;
        private int _currentProductId;
        private string _currentBatchNumber = string.Empty;
        private DateTime? _currentProductionDate;
        private DateTime? _currentExpirationDate;

        private readonly Dictionary<string, string> _globalVariables = new Dictionary<string, string>();

        public event EventHandler<DetectionResult> DetectionResult;

        /// <summary>方案崩溃事件（SimulateSolutionCrash 触发，模拟 VM 工作状态异常）</summary>
        public event EventHandler SolutionCrashed;

        public bool IsSolutionLoaded => _isSolutionLoaded;
        public bool HasProcedure => _isSolutionLoaded && !string.IsNullOrEmpty(_currentProcedureName);
        public bool IsContinuousRun => _continuousTimer != null;

        /// <summary>最近一次出图时间（ICameraHeartbeat，相机心跳监控用）</summary>
        public DateTime? LastFrameTime => _lastFrameTime;

        public MockDetectionEngine(LoggingService logService, IDetectionRecordService detectionRecordService, ImageArchiver imageArchiver, StationContext stationContext)
        {
            _logService = logService;
            _detectionRecordService = detectionRecordService;
            _imageArchiver = imageArchiver;
            _stationContext = stationContext;
            // 相机心跳：方案加载且相机在线时每秒刷新出图时间戳（模拟采集模块出帧）
            _heartbeatTimer = new Timer(_ =>
            {
                if (_cameraAlive && _isSolutionLoaded)
                    _lastFrameTime = DateTime.Now;
            }, null, HeartbeatIntervalMs, HeartbeatIntervalMs);
        }

        /// <summary>模拟相机离线（心跳停止刷新），用于测试 CameraHealthMonitor 报警。</summary>
        public void SimulateCameraLoss()
        {
            _cameraAlive = false;
            _logService?.Log("[Mock] 模拟相机离线（心跳停止）");
        }

        /// <summary>模拟相机恢复（心跳恢复刷新）。</summary>
        public void SimulateCameraRestore()
        {
            _cameraAlive = true;
            _lastFrameTime = DateTime.Now;
            _logService?.Log("[Mock] 模拟相机恢复（心跳恢复）");
        }

        /// <summary>模拟方案崩溃：卸载方案并触发 SolutionCrashed 事件，用于测试崩溃自动恢复。</summary>
        public void SimulateSolutionCrash()
        {
            _isSolutionLoaded = false;
            _currentProcedureName = string.Empty;
            _logService?.Log("[Mock] 模拟方案崩溃");
            SolutionCrashed?.Invoke(this, EventArgs.Empty);
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
            // 每次检测视为一次出图，刷新相机心跳时间戳
            _lastFrameTime = DateTime.Now;

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

                    // 顺序号与工位码：图片文件名与数据库记录共用同一组值
                    int seq = _stationContext?.NextSequence() ?? 0;
                    string station = _stationContext?.GetStationCode() ?? "UNKNOWN";

                    // 模拟 NG 缺陷分类与置信度（Phase 0 演示用；真实值来自 v2 协议）
                    var ngReason = isOK ? NgReason.None : NgReasonPool[_random.Next(NgReasonPool.Length)];
                    double confidence = 0.80 + _random.NextDouble() * 0.19;

                    string imagePath = string.Empty;
                    try
                    {
                        using var bmp = CreateMockImage(isOK, batch, mfg, exp);
                        imagePath = _imageArchiver?.SaveImage(bmp, isOK, _currentTaskId, seq, batch, station) ?? string.Empty;
                    }
                    catch (Exception imgEx)
                    {
                        _logService?.Log($"[Mock] 图像保存失败: {imgEx.Message}", "ERROR");
                    }

                    var result = new DetectionResult
                    {
                        IsOK = isOK,
                        BatchNumber = batch,
                        ProductionDate = mfg,
                        ExpirationDate = exp,
                        ImagePath = imagePath,
                        DetectionTime = DateTime.Now,
                        ElapsedMs = sw.ElapsedMilliseconds,
                        NgReason = ngReason,
                        NgDetail = isOK ? string.Empty : DescribeNg(ngReason),
                        Confidence = Math.Round(confidence, 3),
                        CameraId = "CAM1"
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
                            ImagePath = imagePath,
                            ElapsedMs = sw.ElapsedMilliseconds,
                            DetectionTime = DateTime.Now,
                            NgReason = ngReason,
                            NgDetail = result.NgDetail,
                            Confidence = result.Confidence,
                            CameraId = result.CameraId,
                            OperatorId = TripleDetection.Domain.SessionManager.CurrentUserId,
                            StationCode = station,
                            SequenceNo = seq
                        };
                        _detectionRecordService?.Save(record);
                    }
                    catch (Exception ex)
                    {
                        _logService?.Log($"[Mock] DetectionRecord 保存失败: {ex.Message}", "ERROR");
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

        /// <summary>NG 缺陷分类的中文描述（模拟数据用）</summary>
        private static string DescribeNg(NgReason reason)
        {
            return reason switch
            {
                NgReason.BnMismatch => "批号不匹配（模拟）",
                NgReason.MfgMismatch => "生产日期不匹配（模拟）",
                NgReason.ExpMismatch => "有效期不匹配（模拟）",
                NgReason.BnMissing => "批号漏印（模拟）",
                NgReason.MfgMissing => "生产日期漏印（模拟）",
                NgReason.ExpMissing => "有效期漏印（模拟）",
                NgReason.OcrFailed => "OCR 识别失败（模拟）",
                _ => reason.ToString()
            };
        }

        /// <summary>生成一张带 OK/NG 标识的模拟检测图像</summary>
        private static Bitmap CreateMockImage(bool isOK, string batch, string mfg, string exp)
        {
            var bmp = new Bitmap(640, 480);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.White);

            using var titleFont = new Font("Arial", 56, FontStyle.Bold);
            using var titleBrush = new SolidBrush(isOK ? Color.FromArgb(0, 160, 0) : Color.FromArgb(220, 0, 0));
            g.DrawString(isOK ? "OK" : "NG", titleFont, titleBrush, 250, 40);

            using var infoFont = new Font("Arial", 16);
            using var infoBrush = new SolidBrush(Color.Black);
            g.DrawString($"批次: {batch}", infoFont, infoBrush, 40, 180);
            g.DrawString($"生产日期: {mfg}", infoFont, infoBrush, 40, 220);
            g.DrawString($"有效期至: {exp}", infoFont, infoBrush, 40, 260);
            g.DrawString($"[模拟检测] {DateTime.Now:yyyy-MM-dd HH:mm:ss}", infoFont, infoBrush, 40, 300);

            using var frame = new Pen(Color.Gray, 2);
            g.DrawRectangle(frame, 10, 10, 620, 460);
            return bmp;
        }
    }
}
