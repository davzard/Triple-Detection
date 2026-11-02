using System;
using System.Collections.Generic;
using System.Diagnostics;
using Prism.Events;
using System.Drawing;
using TripleDetection.Presentation.Events;
using TripleDetection.Presentation.Models;
using TripleDetection.Domain.Entities;
using TripleDetection.Application.Services;

namespace TripleDetection.Application.VmServices
{
#if VM_SDK
using System.Threading;
using VM.Core;
using VM.PlatformSDKCS;
using GlobalVariableModuleCs;
using TripleDetection.Application.Line;
    /// <summary>
    /// VisionMaster 4.x 真实检测引擎。仅在 VM_SDK 宏启用（SDK DLL 齐全）时编译。
    /// 相机心跳（ICameraHeartbeat）：方案加载且未崩溃时定时刷新出图时间戳（采集模块出帧的代理指标；
    /// 有 VM 环境后可替换为采集模块帧计数/时间戳），结果回调时同步刷新。
    /// 方案崩溃（SolutionCrashed）：OnWorkStatusEvent 收到错误工作状态时触发，由编排器自动重载。
    /// </summary>
    public class VmIntegrationService : IDetectionEngine, ICameraHeartbeat
    {
        private VmProcedure _procedure;
        private volatile bool _isSolutionLoad = false;
        private DateTime? _lastFrameTime;
        private Timer _heartbeatTimer;
        private LoggingService _logService;
        private IDetectionRecordService _detectionRecordService;
        private IEventAggregator _eventAggregator;
        private StationContext _stationContext;
        private int _currentTaskId;
        private int _currentProductId;
        private string _currentBatchNumber;

        public event EventHandler<DetectionResult> DetectionResult;

        /// <summary>方案崩溃事件（OnWorkStatusEvent 错误状态触发）</summary>
        public event EventHandler SolutionCrashed;

        /// <summary>最近一次出图时间（ICameraHeartbeat，相机心跳监控用）</summary>
        public DateTime? LastFrameTime => _lastFrameTime;

        // 注：VM 回调取图存盘（ImageArchiver）在后续阶段接通，届时再注入 ImageArchiver
        public VmIntegrationService(LoggingService logService, IDetectionRecordService detectionRecordService, IEventAggregator eventAggregator = null, StationContext stationContext = null)
        {
            _logService = logService;
            _detectionRecordService = detectionRecordService;
            _eventAggregator = eventAggregator;
            _stationContext = stationContext;
            VmSolution.OnWorkStatusEvent += VmSolution_OnWorkStatusEvent;
            // 相机心跳：方案加载且未崩溃时每秒刷新出图时间戳（代理指标）
            _heartbeatTimer = new Timer(_ =>
            {
                if (_isSolutionLoad)
                    _lastFrameTime = DateTime.Now;
            }, null, 1000, 1000);
        }

        public void LoadSolution(string solPath)
        {
            VmSolution.Load(solPath);
            _isSolutionLoad = true;
            _lastFrameTime = DateTime.Now;

            ProcessInfoList processList = VmSolution.Instance.GetAllProcedureList();
            if (processList.nNum > 0)
            {
                _procedure = VmSolution.Instance[processList.astProcessInfo[0].strProcessName] as VmProcedure;
            }
        }

        public void RunOnce()
        {
            _stopwatch.Restart();
            _procedure?.Run();
        }

        public void Stop()
        {
            if (_procedure != null)
            {
                _procedure.ContinuousRunEnable = false;
            }
        }

        public bool IsContinuousRun => _procedure?.ContinuousRunEnable ?? false;

        public void SetProcedure(string procedureName)
        {
            if (_isSolutionLoad && !string.IsNullOrEmpty(procedureName))
            {
                _procedure = VmSolution.Instance[procedureName] as VmProcedure;
            }
        }

        public void SetContinuousRun(bool enable)
        {
            if (_procedure != null)
            {
                _procedure.ContinuousRunEnable = enable;
            }
        }

        public void SetCurrentTaskContext(int taskId, int productId, string batchNumber, DateTime? productionDate, DateTime? expirationDate)
        {
            _currentTaskId = taskId;
            _currentProductId = productId;
            _currentBatchNumber = batchNumber ?? "";
        }

        public bool IsSolutionLoaded => _isSolutionLoad;

        public bool HasProcedure => _procedure != null;

        public string GetGlobalVariableString(string name)
        {
            if (!_isSolutionLoad) return null;
            var gvTool = VmSolution.Instance["全局变量1"] as GlobalVariableModuleTool;
            return gvTool?.GetGlobalVar(name);
        }

        public VmProcedure GetProcedure()
        {
            return _procedure;
        }

        public List<string> GetAllProcedureNames()
        {
            var names = new List<string>();
            if (_isSolutionLoad)
            {
                var processList = VmSolution.Instance.GetAllProcedureList();
                for (int i = 0; i < processList.nNum; i++)
                {
                    names.Add(processList.astProcessInfo[i].strProcessName);
                }
            }
            return names;
        }

        public void SetGlobalVariableString(string name, string value)
        {
            if (_procedure != null)
            {
                // var gvTool = _procedure.Modules["GlobalVariable"] as GlobalVariableModuleTool;
                var gvTool =  VmSolution.Instance["全局变量1"] as GlobalVariableModuleTool;
                if (gvTool != null)
                {
                    string defaultValue = gvTool.GetGlobalVar(name) ?? "null";
                    gvTool.SetGlobalVar(name, value);
                    _logService?.Log($"[VM GlobalVariable] {name}: '{defaultValue}' -> '{value}'");
                }
            }
        }

        private Stopwatch _stopwatch = new Stopwatch();

        private void VmSolution_OnWorkStatusEvent(ImvsSdkDefine.IMVS_MODULE_WORK_STAUS workStatusInfo)
        {
            _logService?.Log($"[Callback] nWorkStatus={workStatusInfo.nWorkStatus}, nProcessID={workStatusInfo.nProcessID}");
            // 错误工作状态 → 方案崩溃（编排器订阅后自动重载，次数上限，超限 Alarm 2002）
            if (workStatusInfo.nWorkStatus != 0)
            {
                _isSolutionLoad = false;
                _logService?.Log($"[Callback] 方案崩溃（nWorkStatus={workStatusInfo.nWorkStatus}），已触发 SolutionCrashed");
                SolutionCrashed?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (workStatusInfo.nWorkStatus == 0 && workStatusInfo.nProcessID == 10000)
            {
                _logService?.Log("[Callback] 进入回调处理");
                _lastFrameTime = DateTime.Now; // 出图完成，刷新相机心跳
                _stopwatch.Stop();
                try
                {
                    if (_procedure == null)
                    {
                        Debug.WriteLine("VmIntegrationService: _procedure is null");
                        _logService?.Log("[Callback] _procedure is null");
                        return;
                    }
                    _logService?.Log("[Callback] _procedure ok");

                    var ioNameInfos = _procedure.ModuResult.GetAllOutputNameInfo();
                    _logService?.Log($"[Callback] ioNameInfos.Count={ioNameInfos.Count}");
                    if (ioNameInfos.Count == 0)
                    {
                        Debug.WriteLine("VmIntegrationService: no outputs available");
                        return;
                    }

                    if (ioNameInfos[0].TypeName != IMVS_MODULE_BASE_DATA_TYPE.IMVS_GRAP_TYPE_STRING)
                    {
                        Debug.WriteLine($"VmIntegrationService: type mismatch, got {ioNameInfos[0].TypeName}");
                        return;
                    }

                    var outputResult = _procedure.ModuResult.GetOutputString(ioNameInfos[0].Name);
                    var stringVal = outputResult.astStringVal;
                    _logService?.Log($"[Callback] stringVal={(stringVal == null ? "null" : "ok, len=" + stringVal.Length)}");
                    if (stringVal == null || stringVal.Length == 0)
                    {
                        Debug.WriteLine("VmIntegrationService: stringVal is null or empty");
                        return;
                    }

                    string strResult = stringVal[0].strValue;
                    _logService?.Log($"[Callback] strResult={strResult}");
                    if (strResult != null)
                    {
                        // 协议解析（v1 CSV / v2 JSON 自动路由），解析失败返回 NG 兜底
                        var result = ResultParserRegistry.Parse(strResult);
                        result.ElapsedMs = _stopwatch.ElapsedMilliseconds;

                        // Publish via EventAggregator (preferred)
                        _eventAggregator?.GetEvent<DetectionResultEvent>().Publish(result);

                        // 引擎接口事件
                        DetectionResult?.Invoke(this, result);

                        // 保存 DetectionRecord（非阻塞，异常吞噬）
                        try
                        {
                            var record = new Domain.Entities.DetectionRecord
                            {
                                TaskId = _currentTaskId,
                                ProductId = _currentProductId,
                                BatchNumber = result.BatchNumber,
                                IsOK = result.IsOK,
                                ProductionDate = result.ProductionDate,
                                ExpirationDate = result.ExpirationDate,
                                ImagePath = result.ImagePath,
                                ElapsedMs = result.ElapsedMs,
                                DetectionTime = DateTime.Now,
                                NgReason = result.NgReason,
                                NgDetail = result.NgDetail,
                                Confidence = result.Confidence,
                                CameraId = result.CameraId,
                                OperatorId = TripleDetection.Domain.SessionManager.CurrentUserId,
                                StationCode = _stationContext?.GetStationCode() ?? "UNKNOWN",
                                SequenceNo = _stationContext?.NextSequence() ?? 0
                            };
                            _detectionRecordService?.Save(record);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"DetectionRecord保存失败: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // VM 回调异常必须落日志（此前仅 Debug.WriteLine，生产环境不可见）
                    dynamic vmEx = ex;
                    if (vmEx.errorCode != null)
                        _logService?.Log($"[VM Error] 0x{vmEx.errorCode:X} {ex.Message}", "ERROR");
                    else
                        _logService?.Log($"[VM Error] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", "ERROR");
                }
                finally
                {
                    _stopwatch.Restart();
                }
            }
        }
    }
#endif
}
