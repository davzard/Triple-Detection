using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Prism.Events;
using TripleDetection.Application.Services;

namespace TripleDetection.Application.VmServices
{
    /// <summary>
    /// 检测图片归档器（生产级命名与目录结构）。
    /// 目录：Images/{yyyyMMdd}/{OK|NG}/
    /// 命名：{yyyyMMdd_HHmmss_fff}_{StationCode}_{TaskId}_{Batch}_{Seq:000000}_{OK|NG}.png
    /// 文件名即追溯元数据，支持按日期/工位/任务/批号检索。
    /// 保存失败记录 ERROR 日志并返回 null（不抛异常，不阻塞检测链路）。
    /// </summary>
    public class ImageArchiver
    {
        private readonly IEventAggregator? _eventAggregator;
        private readonly LoggingService? _logService;
        private readonly string _baseDirectory;
        private readonly object _saveLock = new object();

        /// <param name="baseDirectory">图片根目录；默认 BaseDirectory（测试可注入临时目录）</param>
        public ImageArchiver(IEventAggregator? eventAggregator = null, LoggingService? logService = null, string? baseDirectory = null)
        {
            _eventAggregator = eventAggregator;
            _logService = logService;
            _baseDirectory = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// 保存检测图片。
        /// </summary>
        /// <param name="bitmap">待保存位图（不可为 null）</param>
        /// <param name="isOK">检测结果</param>
        /// <param name="taskId">任务 ID</param>
        /// <param name="sequenceNo">检测顺序号</param>
        /// <param name="batchNumber">批号（非法文件名字符会被替换）</param>
        /// <param name="stationCode">工位码</param>
        /// <returns>完整路径；失败返回 null</returns>
        public string? SaveImage(Bitmap bitmap, bool isOK, int taskId, int sequenceNo, string batchNumber, string stationCode)
        {
            if (bitmap == null)
            {
                return null;
            }

            try
            {
                string dateFolder = DateTime.Now.ToString("yyyyMMdd");
                string resultFolder = isOK ? "OK" : "NG";
                string folder = Path.Combine(_baseDirectory, "Images", dateFolder, resultFolder);
                Directory.CreateDirectory(folder);

                string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}" +
                                  $"_{Sanitize(stationCode)}" +
                                  $"_{taskId}" +
                                  $"_{Sanitize(batchNumber)}" +
                                  $"_{sequenceNo:D6}" +
                                  $"_{resultFolder}.png";
                string fullPath = Path.Combine(folder, fileName);

                // 并发保护：VM 回调可能从不同线程触发
                lock (_saveLock)
                {
                    bitmap.Save(fullPath, ImageFormat.Png);
                }
                return fullPath;
            }
            catch (Exception ex)
            {
                _logService?.Log($"[ImageArchiver] 图片保存失败: {ex.Message}", "ERROR");
                return null;
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "NA";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = s.ToCharArray();
            foreach (var c in invalid)
            {
                for (int i = 0; i < chars.Length; i++)
                {
                    if (chars[i] == c) chars[i] = '_';
                }
            }
            var result = new string(chars).Trim();
            return result.Length == 0 ? "NA" : result;
        }
    }
}
