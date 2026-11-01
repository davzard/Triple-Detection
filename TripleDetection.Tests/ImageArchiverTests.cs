using System;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using TripleDetection.Application.VmServices;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>
    /// 图片归档器测试：目录 Images/{yyyyMMdd}/{OK|NG}/ 与
    /// 文件命名 {yyyyMMdd_HHmmss_fff}_{StationCode}_{TaskId}_{Batch}_{Seq:D6}_{OK|NG}.png
    /// </summary>
    public class ImageArchiverTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "td_img_tests_" + Guid.NewGuid().ToString("N"));
        private readonly ImageArchiver _archiver;

        public ImageArchiverTests()
        {
            Directory.CreateDirectory(_tempDir);
            _archiver = new ImageArchiver(baseDirectory: _tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { /* 清理失败不影响测试结论 */ }
        }

        private static Bitmap NewBitmap() => new Bitmap(8, 8);

        [Fact]
        public void SaveImage_OK_目录与命名正确()
        {
            using var bmp = NewBitmap();
            string path = _archiver.SaveImage(bmp, true, taskId: 7, sequenceNo: 42, batchNumber: "BN20260901", stationCode: "F1-L2");

            Assert.NotNull(path);
            Assert.True(File.Exists(path));

            // 目录结构：Images/{yyyyMMdd}/OK/
            string expectedFolder = Path.Combine(_tempDir, "Images", DateTime.Now.ToString("yyyyMMdd"), "OK");
            Assert.StartsWith(expectedFolder + Path.DirectorySeparatorChar, path);

            // 文件命名
            string fileName = Path.GetFileName(path);
            var match = Regex.Match(fileName,
                @"^(\d{8})_\d{6}_\d{3}_(.+)_7_(BN20260901)_(\d{6})_OK\.png$");
            Assert.True(match.Success, $"文件名不符合规范: {fileName}");
            Assert.Equal(DateTime.Now.ToString("yyyyMMdd"), match.Groups[1].Value);
            Assert.Equal("F1-L2", match.Groups[2].Value);
            Assert.Equal("BN20260901", match.Groups[3].Value);
            Assert.Equal("000042", match.Groups[4].Value);
        }

        [Fact]
        public void SaveImage_NG_落入NG目录()
        {
            using var bmp = NewBitmap();
            string path = _archiver.SaveImage(bmp, false, taskId: 1, sequenceNo: 1, batchNumber: "BN001", stationCode: "F1-L2");

            Assert.NotNull(path);
            Assert.Contains(
                Path.Combine("Images", DateTime.Now.ToString("yyyyMMdd"), "NG"),
                path.Replace(_tempDir + Path.DirectorySeparatorChar, string.Empty));
            Assert.EndsWith("_NG.png", path);
        }

        [Fact]
        public void SaveImage_批号含非法字符_被替换()
        {
            using var bmp = NewBitmap();
            string path = _archiver.SaveImage(bmp, true, taskId: 1, sequenceNo: 1, batchNumber: "BN/001:002", stationCode: "F1-L2");

            Assert.NotNull(path);
            string fileName = Path.GetFileName(path);
            Assert.DoesNotContain("/", fileName);
            Assert.DoesNotContain(":", fileName);
            Assert.Contains("BN_001_002", fileName);
        }

        [Fact]
        public void SaveImage_空批号_记为NA()
        {
            using var bmp = NewBitmap();
            string path = _archiver.SaveImage(bmp, true, taskId: 1, sequenceNo: 1, batchNumber: string.Empty, stationCode: "F1-L2");

            Assert.NotNull(path);
            Assert.Contains("_NA_", Path.GetFileName(path));
        }

        [Fact]
        public void SaveImage_null位图_返回null()
        {
            Assert.Null(_archiver.SaveImage(null, true, 1, 1, "BN001", "F1-L2"));
        }

        [Fact]
        public void SaveImage_不同序列号_文件不重名()
        {
            using var bmp = NewBitmap();
            string p1 = _archiver.SaveImage(bmp, true, 1, 1, "BN001", "F1-L2");
            string p2 = _archiver.SaveImage(bmp, true, 1, 2, "BN001", "F1-L2");

            Assert.NotNull(p1);
            Assert.NotNull(p2);
            Assert.NotEqual(p1, p2);
            Assert.True(File.Exists(p1));
            Assert.True(File.Exists(p2));
        }
    }
}
