using TripleDetection.Domain.Enums;

namespace TripleDetection.Domain.Entities;

public class DetectionRecord : BaseEntity
{
    public int TaskId { get; set; }
    public int ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public bool IsOK { get; set; }
    public string ProductionDate { get; set; } = string.Empty;
    public string ExpirationDate { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }
    public DateTime DetectionTime { get; set; }

    // ---- 生产级追溯扩展（Phase 0，见 docs/specs/2026-11-01-production-readiness-design.md §3.5）----
    /// <summary>NG 缺陷分类，OK 时为 None</summary>
    public NgReason NgReason { get; set; } = NgReason.None;
    /// <summary>缺陷明细（v2 协议的 msg / v1 原始串）</summary>
    public string NgDetail { get; set; } = string.Empty;
    /// <summary>当班操作员（开班时确定；Phase 0 取当前登录用户）</summary>
    public int OperatorId { get; set; }
    /// <summary>工位码（FactoryCode+ProductionLine）</summary>
    public string StationCode { get; set; } = string.Empty;
    /// <summary>相机标识，多相机预留，默认 CAM1</summary>
    public string CameraId { get; set; } = "CAM1";
    /// <summary>OCR 置信度（v2 协议提供；v1 为 null）</summary>
    public double? Confidence { get; set; }
    /// <summary>顺序号（Phase 0 为进程内计数；Phase 3 改为班次内顺序号）</summary>
    public int SequenceNo { get; set; }
}