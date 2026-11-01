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
using VM.Core;
using VM.PlatformSDKCS;
using GlobalVariableModuleCs;
    /// <summary>
    /// VisionMaster 4.x 真实检测引擎。仅在 VM_SDK 宏启用（SDK DLL 齐全）时编译。
    /// </summary>
    public class VmIntegrationService : IDetectionEngine
    {
        private VmProcedure _procedure;
        private bool _isSolutionLoad = false;
        private LoggingService _logService;
        private IDetectionRecordService _detectionRecordService;
        private IEventAggregator _eventAggregator;
        private int _currentTaskId;
        private int _currentProductId;
        private string _currentBatchNumber;

        public event EventHandler<DetectionResult> DetectionResult;

        public VmIntegrationService(LoggingService logService, IDetectionRecordService detectionRecordService, IEventAggregator eventAggregator = null)
        {
            _logService = logService;
            _detectionRecordService = detectionRecordService;
            _eventAggregator = eventAggregator;
            VmSolution.OnWorkStatusEvent += VmSolution_OnWorkStatusEvent;
        }

        public void LoadSolution(string solPath)
        {
            VmSolution.Load(solPath);
            _isSolutionLoad = true;

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
            if (workStatusInfo.nWorkStatus == 0 && workStatusInfo.nProcessID == 10000)
            {
                _logService?.Log("[Callback] 进入回调处理");
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
                        var result = ParseResult(strResult);
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
                                DetectionTime = DateTime.Now
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
                    dynamic vmEx = ex;
                    if (vmEx.errorCode != null)
                        Debug.WriteLine($"VmIntegrationService VM Error: 0x{vmEx.errorCode:X}");
                    else
                        Debug.WriteLine($"VmIntegrationService Error: {ex.Message}");
                }
                finally
                {
                    _stopwatch.Restart();
                }
            }
        }

        internal static DetectionResult ParseResult(string strResult)
        {
            if (string.IsNullOrEmpty(strResult))
            {
                return new DetectionResult
                {
                    IsOK = false,
                    ErrorMessage = "Empty result string",
                    DetectionTime = DateTime.Now
                };
            }

            var parts = strResult.Trim().Split(',');

            if (parts.Length < 4)
            {
                return new DetectionResult
                {
                    IsOK = false,
                    ErrorMessage = $"Invalid result format: expected 4 fields, got {parts.Length}",
                    DetectionTime = DateTime.Now
                };
            }

            return new DetectionResult
            {
                IsOK = parts[0].Trim() == "1",
                BatchNumber = parts[1].Trim(),
                ProductionDate = parts[2].Trim(),
                ExpirationDate = parts[3].Trim(),
                DetectionTime = DateTime.Now
            };
        }
    }
#endif
}
