using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TripleDetection.Application.Security;
using TripleDetection.Application.Services;
using TripleDetection.Domain.Enums;

namespace TripleDetection.Infrastructure.Persistence
{
    /// <summary>
    /// SQLite 数据库备份（Phase 2，设计文档 §3.5）：
    /// 通过 VACUUM INTO 生成一致性备份文件（Data/backup/），仅保留最近 N 份。
    /// </summary>
    public class DatabaseBackupService
    {
        private readonly LoggingService _logService;
        private readonly string _dbPath;
        private readonly string _backupDir;
        private readonly IAuthorizationService? _authz;

        public DatabaseBackupService(string dbPath, LoggingService logService, string? backupDir = null, IAuthorizationService? authz = null)
        {
            _dbPath = dbPath;
            _logService = logService;
            _backupDir = backupDir ?? Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "backup");
            _authz = authz;
        }

        /// <summary>
        /// 执行一次备份并修剪旧备份。
        /// </summary>
        /// <param name="keepCount">保留份数</param>
        /// <returns>备份文件路径；失败返回 null</returns>
        public string? Backup(int keepCount)
        {
            // 越权校验在 try 外：AuthorizationException 不被吞掉
            _authz?.Require(AuthorizationAction.BackupRun);
            try
            {
                if (!File.Exists(_dbPath))
                {
                    _logService?.LogWarn("维护", $"数据库备份跳过：{_dbPath} 不存在");
                    return null;
                }
                Directory.CreateDirectory(_backupDir);

                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFile = Path.Combine(_backupDir, $"tripledetection_{stamp}.db");

                using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    // 路径内单引号需双写转义
                    var quoted = "'" + backupFile.Replace("'", "''") + "'";
                    cmd.CommandText = $"VACUUM INTO {quoted}";
                    cmd.ExecuteNonQuery();
                }

                _logService?.LogInfo("维护", $"数据库备份完成：{backupFile}");
                Prune(keepCount);
                return backupFile;
            }
            catch (Exception ex)
            {
                _logService?.LogError("维护", $"数据库备份失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>按文件名时间戳保留最近 keepCount 份，删除更旧的。</summary>
        private void Prune(int keepCount)
        {
            try
            {
                if (keepCount <= 0) return;
                var stale = Directory.GetFiles(_backupDir, "tripledetection_*.db")
                    .OrderByDescending(f => f) // 文件名含 yyyyMMdd_HHmmss，字典序即时间序
                    .Skip(keepCount);
                foreach (var f in stale)
                {
                    File.Delete(f);
                    _logService?.LogInfo("维护", $"数据库备份修剪：删除 {Path.GetFileName(f)}");
                }
            }
            catch (Exception ex)
            {
                _logService?.LogWarn("维护", $"数据库备份修剪失败：{ex.Message}");
            }
        }
    }
}
