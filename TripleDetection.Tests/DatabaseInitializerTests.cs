using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;
using TripleDetection.Infrastructure.Persistence;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>
    /// 数据库版本化迁移测试：
    /// ① 全新库自动建表 + 两个迁移 ② 旧版库（无 SchemaMigrations/无追溯列）启动时自动升级
    /// ③ 重复执行幂等
    /// </summary>
    public class DatabaseInitializerTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "td_tests_" + Guid.NewGuid().ToString("N"));

        public DatabaseInitializerTests()
        {
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { /* 清理失败不影响测试结论 */ }
        }

        private string DbPath(string name = "test.db") => Path.Combine(_tempDir, name);

        /// <summary>用 v1 基线建表语句创建一个"旧版"数据库（无 SchemaMigrations、无追溯列）</summary>
        private void CreateLegacyDb(string path)
        {
            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE Products (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Description TEXT,
    Code TEXT, SolFilePath TEXT, ValidType INTEGER, ValidPeriod INTEGER, Status INTEGER,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);
CREATE TABLE Tasks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, ProductId INTEGER,
    Status INTEGER NOT NULL DEFAULT 0, CreatedBy TEXT, ReviewedBy TEXT, ReviewedAt TEXT,
    ProductionDate TEXT, ExpirationDate TEXT, BatchNumber TEXT,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);
CREATE TABLE DetectionRecords (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, TaskId INTEGER, ProductId INTEGER,
    BatchNumber TEXT, IsOK INTEGER NOT NULL DEFAULT 0, ProductionDate TEXT, ExpirationDate TEXT,
    ImagePath TEXT, ElapsedMs INTEGER NOT NULL DEFAULT 0, DetectionTime TEXT NOT NULL,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);
CREATE TABLE SystemConfigs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, Category TEXT NOT NULL, ConfigKey TEXT NOT NULL,
    ConfigValue TEXT, Description TEXT,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);
CREATE TABLE Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, Username TEXT NOT NULL UNIQUE,
    RealName TEXT, Password TEXT, PasswordSalt TEXT, PasswordHash TEXT,
    Role TEXT NOT NULL, IsEnabled INTEGER NOT NULL DEFAULT 1, IsLocked INTEGER NOT NULL DEFAULT 0,
    LastLoginAt TEXT,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);
CREATE TABLE AuditLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId INTEGER NOT NULL, UserName TEXT NOT NULL,
    Action TEXT NOT NULL, ObjectType TEXT NOT NULL, ObjectId INTEGER NOT NULL,
    Details TEXT, IpAddress TEXT,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);";
            cmd.ExecuteNonQuery();
        }

        private HashSet<string> GetColumns(string path, string table)
        {
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                cols.Add(reader.GetString(1));
            }
            return cols;
        }

        private List<int> GetAppliedVersions(string path)
        {
            var versions = new List<int>();
            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Version FROM SchemaMigrations ORDER BY Version";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                versions.Add(reader.GetInt32(0));
            }
            return versions;
        }

        [Fact]
        public void Initialize_全新库_应用全部迁移()
        {
            string path = DbPath("fresh.db");
            DatabaseInitializer.Initialize(path);

            Assert.Equal(new[] { 1, 2, 3 }, GetAppliedVersions(path));
            var cols = GetColumns(path, "DetectionRecords");
            Assert.Contains("NgReason", cols);
            Assert.Contains("NgDetail", cols);
            Assert.Contains("StationCode", cols);
            Assert.Contains("SequenceNo", cols);

            // v3：Shifts 表（Phase 2 班次实体）
            var shiftCols = GetColumns(path, "Shifts");
            Assert.Contains("StartAt", shiftCols);
            Assert.Contains("OkCount", shiftCols);
            Assert.Contains("NgCount", shiftCols);
        }

        [Fact]
        public void Initialize_旧版库_自动升级并保留数据()
        {
            string path = DbPath("legacy.db");
            CreateLegacyDb(path);

            // 旧库中放一条历史记录，升级后必须保留
            using (var seedConn = new SqliteConnection($"Data Source={path}"))
            {
                seedConn.Open();
                using var seedCmd = seedConn.CreateCommand();
                seedCmd.CommandText = @"INSERT INTO DetectionRecords
                    (TaskId, BatchNumber, IsOK, DetectionTime, IsDeleted, CreateAt, UpdateAt)
                    VALUES (1, 'LEGACY-BATCH-001', 1, '2026-01-01T00:00:00', 0, '2026-01-01T00:00:00', '2026-01-01T00:00:00')";
                seedCmd.ExecuteNonQuery();
            }

            DatabaseInitializer.Initialize(path);

            Assert.Equal(new[] { 1, 2, 3 }, GetAppliedVersions(path));
            var cols = GetColumns(path, "DetectionRecords");
            Assert.Contains("NgReason", cols);
            Assert.Contains("CameraId", cols);

            // 历史数据保留，且新列取默认值
            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT BatchNumber, NgReason, CameraId FROM DetectionRecords WHERE BatchNumber = 'LEGACY-BATCH-001'";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("LEGACY-BATCH-001", reader.GetString(0));
            Assert.Equal(0, reader.GetInt32(1));
            Assert.Equal("CAM1", reader.GetString(2));
        }

        [Fact]
        public void Initialize_重复执行_幂等()
        {
            string path = DbPath("idem.db");
            DatabaseInitializer.Initialize(path);
            DatabaseInitializer.Initialize(path); // 第二次不应抛异常

            Assert.Equal(3, GetAppliedVersions(path).Count);
            var cols = GetColumns(path, "DetectionRecords");
            Assert.Contains("NgReason", cols);
        }
    }
}
