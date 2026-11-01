using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripleDetection.Domain.Entities;

namespace TripleDetection.Infrastructure.Persistence.Configurations;

public class DetectionRecordConfiguration : IEntityTypeConfiguration<DetectionRecord>
{
    public void Configure(EntityTypeBuilder<DetectionRecord> builder)
    {
        builder.ToTable("DetectionRecords");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.BatchNumber).HasMaxLength(50);
        builder.Property(d => d.ProductionDate).HasMaxLength(50);
        builder.Property(d => d.ExpirationDate).HasMaxLength(50);
        builder.Property(d => d.ImagePath).HasMaxLength(500);
        builder.Property(d => d.TaskId).IsRequired();
        // Phase 0 追溯字段
        builder.Property(d => d.NgReason).HasConversion<int>();
        builder.Property(d => d.NgDetail).HasMaxLength(500);
        builder.Property(d => d.StationCode).HasMaxLength(50);
        builder.Property(d => d.CameraId).HasMaxLength(20);
    }
}