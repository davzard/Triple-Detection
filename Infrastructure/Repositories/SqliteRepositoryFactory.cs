using System.IO;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Repositories;
using TripleDetection.Infrastructure.Persistence;

namespace TripleDetection.Infrastructure.Repositories;

/// <summary>
/// SQLite 仓储工厂：按连接字符串创建各仓储实现。
/// 默认使用应用目录下的 Data\tripledetection.db。
/// </summary>
public class SqliteRepositoryFactory : IRepositoryFactory
{
    private readonly string _connectionString;

    public SqliteRepositoryFactory(string connectionString = null)
    {
        _connectionString = connectionString
            ?? $"Data Source={Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "tripledetection.db")}";
    }

    public DatabaseProviderType ProviderType => DatabaseProviderType.Sqlite;

    public string ConnectionString => _connectionString;

    public TripleDetectionDbContext CreateContext() => new TripleDetectionDbContext(_connectionString);

    public IUnitOfWork CreateUnitOfWork() => new SqliteUnitOfWork(_connectionString);

    public IRepository<T> CreateRepository<T>() where T : BaseEntity
    {
        return new SqliteRepository<T>(CreateContext());
    }

    public IDetectionRecordRepository CreateDetectionRecordRepository()
        => new DetectionRecordRepository(CreateContext());

    public IAuditLogRepository CreateAuditLogRepository()
        => new AuditLogRepository(CreateContext());
}
