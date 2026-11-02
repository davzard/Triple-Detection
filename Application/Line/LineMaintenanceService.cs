using System;
using TripleDetection.Application.Services;
using TripleDetection.Infrastructure.Persistence;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 产线数据维护（Phase 2，设计文档 §3.5）：数据库备份 + 图像保留清理。
    /// 触发时机：应用启动（登录后）、下班（EndShift）。
    /// 全部操作幂等且失败仅记日志，不影响检测主流程。
    /// </summary>
    public class LineMaintenanceService
    {
        private readonly SystemConfigService _config;
        private readonly DatabaseBackupService _backup;
        private readonly ImageRetentionService _images;
        private readonly LoggingService _logService;

        public LineMaintenanceService(SystemConfigService config, DatabaseBackupService backup,
            ImageRetentionService images, LoggingService logService)
        {
            _config = config;
            _backup = backup;
            _images = images;
            _logService = logService;
        }

        /// <summary>执行一次维护：DB 备份（保留 N 份）+ 图像清理（保留 N 天）。可后台调用。</summary>
        public void RunMaintenance()
        {
            int keepCount = _config.GetInt(SystemConfigService.CatBackup, SystemConfigService.KeyKeepCount, 7);
            int retentionDays = _config.GetInt(SystemConfigService.CatImages, SystemConfigService.KeyRetentionDays, 30);

            _logService?.LogInfo("维护", $"开始数据维护：备份保留 {keepCount} 份，图像保留 {retentionDays} 天");
            _backup.Backup(keepCount);
            var (dirs, bytes) = _images.Cleanup(retentionDays);
            if (dirs > 0)
                _logService?.LogInfo("维护", $"图像清理完成：删除 {dirs} 个目录，释放 {bytes / 1024.0 / 1024.0:F1} MB");
            _logService?.LogInfo("维护", "数据维护完成");
        }
    }
}
