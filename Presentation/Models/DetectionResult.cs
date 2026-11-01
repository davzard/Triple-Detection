using System;
using TripleDetection.Domain.Enums;

namespace TripleDetection.Presentation.Models
{
    public class DetectionResult
    {
        public bool IsOK { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public string ProductionDate { get; set; } = string.Empty;
        public string ExpirationDate { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public DateTime DetectionTime { get; set; }
        public long ElapsedMs { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        // ---- 生产级扩展（Phase 0，见 docs/specs/2026-11-01-production-readiness-design.md §3.3）----
        /// <summary>NG 缺陷分类，OK 时为 None</summary>
        public NgReason NgReason { get; set; } = NgReason.None;
        /// <summary>缺陷明细（v2 的 msg / v1 原始串 / 解析错误信息）</summary>
        public string NgDetail { get; set; } = string.Empty;
        /// <summary>OCR 置信度（v2 协议提供；v1 为 null）</summary>
        public double? Confidence { get; set; }
        /// <summary>相机标识，多相机预留，默认 CAM1</summary>
        public string CameraId { get; set; } = "CAM1";
    }
}