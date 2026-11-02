using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TripleDetection.Application.Services;
using TripleDetection.Infrastructure.Persistence;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>
    /// 数据维护测试（Phase 2，设计文档 §3.5 磁盘治理）：
    /// 图像保留清理（按日期目录）+ 数据库备份（VACUUM INTO + 保留份数修剪）。
    /// </summary>
    public class MaintenanceServiceTests : IDisposable
    {
        private readonly string _dir;

        public MaintenanceServiceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "td_maint_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private LoggingService Log() => new LoggingService(Path.Combine(_dir, "logs"), null);

        // ===== 图像保留清理 =====

        [Fact]
        public void 图像清理_超期目录删除_近期保留()
        {
            var root = Path.Combine(_dir, "Images");
            var oldDir = Path.Combine(root, "20200101");                    // 超期
            var recentDir = Path.Combine(root, DateTime.Today.ToString("yyyyMMdd")); // 当天
            var oddDir = Path.Combine(root, "notadate");                    // 非日期目录
            Directory.CreateDirectory(Path.Combine(oldDir, "NG"));
            Directory.CreateDirectory(recentDir);
            Directory.CreateDirectory(oddDir);
            File.WriteAllText(Path.Combine(oldDir, "NG", "a.png"), "x");

            var svc = new ImageRetentionService(Log(), root);
            var (deleted, freed) = svc.Cleanup(30);

            Assert.False(Directory.Exists(oldDir), "超期目录应被删除");
            Assert.True(Directory.Exists(recentDir), "近期目录应保留");
            Assert.True(Directory.Exists(oddDir), "非日期目录不应被删除");
            Assert.Equal(1, deleted);
            Assert.True(freed > 0);
        }

        [Fact]
        public void 图像清理_边界日期_恰好保留期不删()
        {
            var root = Path.Combine(_dir, "Images");
            var boundary = Path.Combine(root, DateTime.Today.AddDays(-30).ToString("yyyyMMdd")); // 恰好 30 天前
            var justOld = Path.Combine(root, DateTime.Today.AddDays(-31).ToString("yyyyMMdd"));  // 31 天前
            Directory.CreateDirectory(boundary);
            Directory.CreateDirectory(justOld);

            var svc = new ImageRetentionService(Log(), root);
            var (deleted, _) = svc.Cleanup(30);

            Assert.True(Directory.Exists(boundary), "恰好保留期边界的目录应保留");
            Assert.False(Directory.Exists(justOld), "超出保留期的目录应删除");
            Assert.Equal(1, deleted);
        }

        [Fact]
        public void 图像清理_目录不存在_安全返回()
        {
            var svc = new ImageRetentionService(Log(), Path.Combine(_dir, "no-such-dir"));
            var (deleted, freed) = svc.Cleanup(30);
            Assert.Equal(0, deleted);
            Assert.Equal(0, freed);
        }

        // ===== 数据库备份 =====

        private string MakeDb(string name)
        {
            var path = Path.Combine(_dir, name);
            DatabaseInitializer.EnsureDatabaseCreated(path);
            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO SystemConfigs (Category, ConfigKey, ConfigValue, CreateBy, UpdateBy, CreateAt, UpdateAt) VALUES ('Test','K','V','t','t','2026-01-01T00:00:00','2026-01-01T00:00:00')";
            cmd.ExecuteNonQuery();
            return path;
        }

        [Fact]
        public void 备份_生成可恢复文件()
        {
            var dbPath = MakeDb("src.db");
            var backupDir = Path.Combine(_dir, "backup");
            var svc = new DatabaseBackupService(dbPath, Log(), backupDir);

            var file = svc.Backup(7);

            Assert.NotNull(file);
            Assert.True(File.Exists(file), "备份文件应存在");

            // 可恢复性：备份文件是合法 SQLite 且含源数据
            using var conn = new SqliteConnection($"Data Source={file}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ConfigValue FROM SystemConfigs WHERE ConfigKey = 'K'";
            Assert.Equal("V", cmd.ExecuteScalar());
        }

        [Fact]
        public void 备份_修剪旧备份_仅保留N份()
        {
            var dbPath = MakeDb("src2.db");
            var backupDir = Path.Combine(_dir, "backup2");
            Directory.CreateDirectory(backupDir);

            // 预置 5 个"历史"备份（文件名含时间戳，字典序即时间序）
            for (int i = 1; i <= 5; i++)
                File.WriteAllText(Path.Combine(backupDir, $"tripledetection_2026010{i}_000000.db"), "old");

            var svc = new DatabaseBackupService(dbPath, Log(), backupDir);
            svc.Backup(3);

            var remaining = Directory.GetFiles(backupDir, "tripledetection_*.db").OrderBy(f => f).ToArray();
            Assert.True(remaining.Length == 3, $"应仅保留最近 3 份，实际 {remaining.Length}");
            // 最新 3 份 = 20260105 + 20260104 + 本次新生成（2026 年以后时间戳）
            Assert.DoesNotContain(remaining, f => f.Contains("20260101"));
            Assert.DoesNotContain(remaining, f => f.Contains("20260102"));
            Assert.DoesNotContain(remaining, f => f.Contains("20260103"));
        }

        [Fact]
        public void 备份_源库不存在_返回null()
        {
            var svc = new DatabaseBackupService(Path.Combine(_dir, "missing.db"), Log());
            Assert.Null(svc.Backup(7));
        }
    }
}
