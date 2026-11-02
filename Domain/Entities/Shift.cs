using System;

namespace TripleDetection.Domain.Entities;

/// <summary>
/// 生产班次（Phase 2，设计文档 §4.1 Shifts 表）。
/// 开班创建、下班关闭；计数在检测过程中增量更新。
/// </summary>
public class Shift : BaseEntity
{
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    /// <summary>当班操作员</summary>
    public int OperatorId { get; set; }
    /// <summary>当班任务</summary>
    public int TaskId { get; set; }
    /// <summary>产线/工位码</summary>
    public string LineCode { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }
    /// <summary>停机累计时长 ms（Phase 4 完善）</summary>
    public long StopDurationMs { get; set; }
}
