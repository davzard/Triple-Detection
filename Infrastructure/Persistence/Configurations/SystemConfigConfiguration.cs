using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripleDetection.Domain.Entities;

namespace TripleDetection.Infrastructure.Persistence.Configurations;

public class SystemConfigConfiguration : IEntityTypeConfiguration<SystemConfig>
{
    public void Configure(EntityTypeBuilder<SystemConfig> builder)
    {
        builder.ToTable("SystemConfigs");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Category).HasMaxLength(100);
        // 列名与迁移 v1 建表 SQL 保持一致（ConfigKey/ConfigValue）
        builder.Property(s => s.Key).HasColumnName("ConfigKey").HasMaxLength(100);
        builder.Property(s => s.Value).HasColumnName("ConfigValue").HasMaxLength(1000);
        builder.Property(s => s.Description).HasMaxLength(500);
    }
}