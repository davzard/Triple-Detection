using System;
using System.IO;
using System.Linq;

namespace TripleDetection.Application.Services
{
    /// <summary>
    /// 检测图像保留清理（Phase 2，设计文档 §3.5 磁盘治理）：
    /// 删除 Images/{yyyyMMdd} 中日期早于保留天数的目录，使磁盘占用收敛。
    /// </summary>
    public class ImageRetentionService
    {
        private readonly LoggingService _logService;
        private readonly string _imagesRoot;

        public ImageRetentionService(LoggingService logService, string? imagesRoot = null)
        {
            _logService = logService;
            _imagesRoot = imagesRoot ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images");
        }

        /// <summary>
        /// 清理超期图像目录。
        /// </summary>
        /// <param name="retentionDays">保留天数（按目录名日期与今天比较）</param>
        /// <returns>(删除目录数, 释放字节数)</returns>
        public (int DeletedDirs, long FreedBytes) Cleanup(int retentionDays)
        {
            int deleted = 0;
            long freed = 0;
            try
            {
                if (!Directory.Exists(_imagesRoot)) return (0, 0);
                var cutoff = DateTime.Today.AddDays(-retentionDays);

                foreach (var dir in Directory.GetDirectories(_imagesRoot))
                {
                    var name = Path.GetFileName(dir);
                    if (!DateTime.TryParseExact(name, "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var day))
                        continue; // 非日期目录不动

                    if (day >= cutoff) continue;

                    try
                    {
                        var size = new DirectoryInfo(dir)
                            .EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                        Directory.Delete(dir, true);
                        deleted++;
                        freed += size;
                        _logService?.LogInfo("维护", $"图像清理：删除 {name}（{size / 1024.0 / 1024.0:F1} MB，早于保留期 {retentionDays} 天）");
                    }
                    catch (Exception ex)
                    {
                        _logService?.LogWarn("维护", $"图像清理：删除 {name} 失败：{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logService?.LogError("维护", $"图像清理异常：{ex.Message}");
            }
            return (deleted, freed);
        }
    }
}
