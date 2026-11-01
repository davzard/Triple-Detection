using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TripleDetection.Infrastructure.Persistence;

/// <summary>
/// 数据库初始化与版本化迁移。
/// 迁移表 SchemaMigrations 记录已应用的版本号；每个迁移幂等（可重复执行），
/// 旧库升级时按版本顺序补齐，新库一次性应用全部迁移。
/// 见 docs/specs/2026-11-01-production-readiness-design.md §3.5。
/// </summary>
public static class DatabaseInitializer
{
    private static readonly string DataDirectory = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Data");

    private static readonly string DbPath = Path.Combine(
        DataDirectory, "tripledetection.db");

    /// <summary>按版本顺序排列的迁移。新增迁移只追加，不修改已有迁移。事务由框架统一开启/提交。</summary>
    private static readonly List<(int Version, string Name, Action<SqliteConnection, SqliteTransaction> Apply)> Migrations = new()
    {
        (1, "baseline", ApplyBaseline),
        (2, "detection-record-traceability", ApplyDetectionRecordTraceability)
    };

    /// <param name="dbPath">数据库路径；默认 Data/tripledetection.db（测试可注入临时路径）</param>
    public static void Initialize(string? dbPath = null)
    {
        var path = dbPath ?? DbPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        EnsureDatabaseCreated(path);
        SeedInitialData(path);
    }

    public static void EnsureDatabaseCreated(string? dbPath = null)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath ?? DbPath}");
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS SchemaMigrations (
    Version INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    AppliedAt TEXT NOT NULL
);";
            cmd.ExecuteNonQuery();
        }

        foreach (var (version, name, apply) in Migrations)
        {
            if (IsApplied(conn, version)) continue;

            using var tx = conn.BeginTransaction();
            try
            {
                apply(conn, tx);

                using var mark = conn.CreateCommand();
                mark.Transaction = tx;
                mark.CommandText = "INSERT INTO SchemaMigrations (Version, Name, AppliedAt) VALUES ($v, $n, $t)";
                mark.Parameters.AddWithValue("$v", version);
                mark.Parameters.AddWithValue("$n", name);
                mark.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                mark.ExecuteNonQuery();

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }

    private static bool IsApplied(SqliteConnection conn, int version)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = $v";
        cmd.Parameters.AddWithValue("$v", version);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>v1：初始基线（与历史版本完全一致的建表语句）</summary>
    private static void ApplyBaseline(SqliteConnection conn, SqliteTransaction tx)
    {
        Execute(conn, tx, @"
CREATE TABLE IF NOT EXISTS Products (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Description TEXT,
    Code TEXT, SolFilePath TEXT, ValidType INTEGER, ValidPeriod INTEGER, Status INTEGER,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);
CREATE TABLE IF NOT EXISTS Tasks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, ProductId INTEGER,
    Status INTEGER NOT NULL DEFAULT 0, CreatedBy TEXT, ReviewedBy TEXT, ReviewedAt TEXT,
    ProductionDate TEXT, ExpirationDate TEXT, BatchNumber TEXT,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);
CREATE TABLE IF NOT EXISTS DetectionRecords (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, TaskId INTEGER, ProductId INTEGER,
    BatchNumber TEXT, IsOK INTEGER NOT NULL DEFAULT 0, ProductionDate TEXT, ExpirationDate TEXT,
    ImagePath TEXT, ElapsedMs INTEGER NOT NULL DEFAULT 0, DetectionTime TEXT NOT NULL,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);
CREATE TABLE IF NOT EXISTS SystemConfigs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, Category TEXT NOT NULL, ConfigKey TEXT NOT NULL,
    ConfigValue TEXT, Description TEXT,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);
CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, Username TEXT NOT NULL UNIQUE,
    RealName TEXT, Password TEXT, PasswordSalt TEXT, PasswordHash TEXT,
    Role TEXT NOT NULL, IsEnabled INTEGER NOT NULL DEFAULT 1, IsLocked INTEGER NOT NULL DEFAULT 0,
    LastLoginAt TEXT,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);
CREATE TABLE IF NOT EXISTS AuditLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId INTEGER NOT NULL, UserName TEXT NOT NULL,
    Action TEXT NOT NULL, ObjectType TEXT NOT NULL, ObjectId INTEGER NOT NULL,
    Details TEXT, IpAddress TEXT,
    IsDeleted INTEGER NOT NULL DEFAULT 0, CreateAt TEXT NOT NULL, UpdateAt TEXT NOT NULL,
    CreateBy TEXT, UpdateBy TEXT
);");
    }

    /// <summary>
    /// v2：DetectionRecords 追溯字段（NgReason/NgDetail/OperatorId/StationCode/CameraId/Confidence/SequenceNo）
    /// + 常用查询索引。列级幂等：仅当 PRAGMA table_info 中不存在该列时执行 ADD COLUMN。
    /// </summary>
    private static void ApplyDetectionRecordTraceability(SqliteConnection conn, SqliteTransaction tx)
    {
        var sb = new StringBuilder();
        AppendColumnIfMissing(sb, conn, tx, "DetectionRecords", "NgReason", "INTEGER NOT NULL DEFAULT 0");
        AppendColumnIfMissing(sb, conn, tx, "DetectionRecords", "NgDetail", "TEXT");
        AppendColumnIfMissing(sb, conn, tx, "DetectionRecords", "OperatorId", "INTEGER NOT NULL DEFAULT 0");
        AppendColumnIfMissing(sb, conn, tx, "DetectionRecords", "StationCode", "TEXT");
        AppendColumnIfMissing(sb, conn, tx, "DetectionRecords", "CameraId", "TEXT NOT NULL DEFAULT 'CAM1'");
        AppendColumnIfMissing(sb, conn, tx, "DetectionRecords", "Confidence", "REAL");
        AppendColumnIfMissing(sb, conn, tx, "DetectionRecords", "SequenceNo", "INTEGER NOT NULL DEFAULT 0");
        sb.AppendLine(@"
CREATE INDEX IF NOT EXISTS IX_Detection_Time ON DetectionRecords (DetectionTime);
CREATE INDEX IF NOT EXISTS IX_Detection_Batch ON DetectionRecords (BatchNumber);");
        Execute(conn, tx, sb.ToString());
    }

    /// <summary>列不存在才生成 ADD COLUMN（运行时按 PRAGMA table_info 判断，保证迁移幂等）。</summary>
    private static void AppendColumnIfMissing(StringBuilder sb, SqliteConnection conn, SqliteTransaction tx, string table, string column, string definition)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragma = conn.CreateCommand())
        {
            pragma.Transaction = tx;
            pragma.CommandText = $"PRAGMA table_info({table})";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(1));
            }
        }

        if (!existing.Contains(column))
        {
            sb.AppendLine($"ALTER TABLE {table} ADD COLUMN {column} {definition};");
        }
    }

    private static void Execute(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static void SeedInitialData(string? dbPath = null)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath ?? DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users";
        var count = Convert.ToInt64(cmd.ExecuteScalar());
        if (count > 0) return;

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        cmd.CommandText = $@"
INSERT INTO Users (Username, RealName, Password, Role, IsEnabled, IsDeleted, CreateAt, UpdateAt)
VALUES ('admin', 'Administrator', 'admin123', 'Admin', 1, 0, '{now}', '{now}');
INSERT INTO Products (Name, Description, Status, IsDeleted, CreateAt, UpdateAt)
VALUES ('OCR检测产品A', '用于OCR文字识别检测', 1, 0, '{now}', '{now}'),
('缺陷检测产品B', '用于表面缺陷检测', 1, 0, '{now}', '{now}'),
('尺寸测量产品C', '用于尺寸测量', 1, 0, '{now}', '{now}');
INSERT INTO Tasks (Name, Status, IsDeleted, CreateAt, UpdateAt)
VALUES ('OCR检测任务-2025-05-01', 1, 0, '{now}', '{now}'),
('缺陷检测任务-2025-05-02', 1, 0, '{now}', '{now}'),
('尺寸测量任务-2025-05-03', 1, 0, '{now}', '{now}'),
('备料任务-待审核', 0, 0, '{now}', '{now}');";
        cmd.ExecuteNonQuery();
    }
}