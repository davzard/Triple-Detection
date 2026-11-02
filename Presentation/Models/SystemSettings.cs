namespace TripleDetection.Presentation.Models
{
    public class SystemSettings
    {
        public string LogSaveMethod { get; set; } = "ByDate";
        public int LogRetentionDays { get; set; } = 30;
        public string LogExportPath { get; set; } = @"D:\Logs\Export";
        public bool AutoCleanLog { get; set; } = true;
        public string FactoryCode { get; set; } = "F001";
        public string ProductionLine { get; set; } = "L001";
        public string DbBackupRoot { get; set; } = @"D:\Database\Backup";
        public int ImageRetentionCount { get; set; } = 1000;
        public bool AutoCleanImages { get; set; } = true;
        // M8 HMI 声音（设计文档 §3.8）
        public bool SoundMuted { get; set; } = false;
        public int SoundVolume { get; set; } = 80;
    }
}