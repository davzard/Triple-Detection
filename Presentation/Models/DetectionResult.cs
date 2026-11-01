using System;

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
    }
}
