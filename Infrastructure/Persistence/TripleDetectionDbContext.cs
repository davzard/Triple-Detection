using Microsoft.EntityFrameworkCore;
using TripleDetection.Domain.Entities;

namespace TripleDetection.Infrastructure.Persistence;

public class TripleDetectionDbContext : DbContext
{
    private readonly string _connectionString;

    public TripleDetectionDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProdTask> ProdTasks => Set<ProdTask>();
    public DbSet<DetectionRecord> DetectionRecords => Set<DetectionRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<Shift> Shifts => Set<Shift>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TripleDetectionDbContext).Assembly);
    }
}