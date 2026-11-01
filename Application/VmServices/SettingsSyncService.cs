using System;
#if VM_SDK
using iMVS_6000PlatformSDKCS.SyncPlatformSDKCS;
#endif

namespace TripleDetection.Application.VmServices
{
    public class SettingsSyncService
    {
        public bool SyncToVisionMaster(CommunicationSettings commSettings, DeviceControlSettings deviceSettings)
        {
            if (commSettings == null)
                throw new ArgumentNullException(nameof(commSettings));
            if (deviceSettings == null)
                throw new ArgumentNullException(nameof(deviceSettings));

#if !VM_SDK
            // 无 VisionMaster SDK：无法同步到 VM，仅提示
            System.Diagnostics.Debug.WriteLine("[SettingsSync] 当前构建未包含 VisionMaster SDK，跳过同步");
            return true;
#else
            try
            {
                var pfsync = new ImvsSdkPFSync();
                var result = pfsync.Start();
                if (result != 0)
                {
                    throw new Exception($"SDK初始化失败: 0x{result:X}");
                }

                // Sync Communication settings
                pfsync.modules.moduleControl.SetGlobalVarValue("CAM_IP", commSettings.CameraIp);
                pfsync.modules.moduleControl.SetGlobalVarValue("CAM_PORT", commSettings.CameraPort.ToString());
                pfsync.modules.moduleControl.SetGlobalVarValue("PLC_IP", commSettings.PlcIp);
                pfsync.modules.moduleControl.SetGlobalVarValue("PLC_PORT", commSettings.PlcPort.ToString());
                pfsync.modules.moduleControl.SetGlobalVarValue("PLC_TYPE", commSettings.PlcType);
                pfsync.modules.moduleControl.SetGlobalVarValue("PLC_BAUD", commSettings.BaudRate.ToString());

                // Sync Device Control settings
                pfsync.modules.moduleControl.SetGlobalVarValue("LIGHT_TYPE", deviceSettings.LightSourceType);
                pfsync.modules.moduleControl.SetGlobalVarValue("CAP_DELAY", deviceSettings.CaptureDelayMs.ToString());
                pfsync.modules.moduleControl.SetGlobalVarValue("CAP_TIMEOUT", deviceSettings.CaptureFeedbackTimeoutMs.ToString());
                pfsync.modules.moduleControl.SetGlobalVarValue("REJ_DELAY", deviceSettings.RejectDelayMs.ToString());
                pfsync.modules.moduleControl.SetGlobalVarValue("REJ_DURATION", deviceSettings.RejectDurationMs.ToString());
                pfsync.modules.moduleControl.SetGlobalVarValue("REJ_COUNT", deviceSettings.ConsecutiveRejectsToStopLine.ToString());

                pfsync.Exit();
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"同步到VisionMaster失败: {ex.Message}", ex);
            }
#endif
        }
    }
}
