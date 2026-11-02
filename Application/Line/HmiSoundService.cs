using System;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 蜂鸣器抽象（设计文档 §3.8 声音）。由表现层提供具体实现（WAV + SoundPlayer），
    /// 便于单元测试用 Fake 替身记录音序。
    /// </summary>
    public interface IAudioBeeper
    {
        /// <summary>播放一个纯音（阻塞或入队由实现决定）。volumePercent 0-100。</summary>
        void PlayTone(int frequencyHz, int durationMs, int volumePercent);
    }

    /// <summary>
    /// HMI 声音服务（设计文档 §3.8）：
    /// OK 短鸣 1 声；NG 长鸣 2 声，且连续 NG 时音调逐级升高（便于操作员察觉异常累积）；
    /// 报警 3 声高頻。可配静音/音量。连续 NG 计数独立于状态机的停线计数，仅用于音调升级。
    /// </summary>
    public class HmiSoundService : IDisposable
    {
        // 音序参数（Hz / ms）
        private const int OkFreq = 1000;
        private const int OkDurationMs = 150;

        private const int NgBaseFreq = 600;
        private const int NgDurationMs = 400;
        private const int NgBeepCount = 2;
        private const int NgEscalationStep = 100;   // 每连续一次 NG 升 100Hz
        private const int NgMaxEscalation = 5;       // 最多升 5 级（600→1100Hz）

        private const int AlarmFreq = 1500;
        private const int AlarmDurationMs = 300;
        private const int AlarmBeepCount = 3;

        private readonly IAudioBeeper _beeper;
        private int _consecutiveNg;

        public HmiSoundService(IAudioBeeper beeper)
        {
            _beeper = beeper ?? throw new ArgumentNullException(nameof(beeper));
        }

        /// <summary>静音（true 时不发声，但仍统计连续 NG）。</summary>
        public bool Muted { get; set; }

        /// <summary>音量百分比 0-100。</summary>
        public int Volume { get; set; } = 80;

        /// <summary>当前连续 NG 数（OK 或复位后清零）。</summary>
        public int ConsecutiveNg
        {
            get { lock (_gate) return _consecutiveNg; }
        }

        private readonly object _gate = new object();

        /// <summary>检测结果发声：OK 短鸣 1 声；NG 长鸣 2 声（音调随连续 NG 升高）。</summary>
        public void ReportResult(bool isOK)
        {
            int ngFreq = NgBaseFreq;
            lock (_gate)
            {
                if (isOK)
                {
                    _consecutiveNg = 0;
                }
                else
                {
                    _consecutiveNg++;
                    // 第 1 次 NG 用基础音调，之后每连续一次升一级
                    int level = Math.Min(_consecutiveNg - 1, NgMaxEscalation);
                    ngFreq = NgBaseFreq + level * NgEscalationStep;
                }
            }

            if (Muted) return;

            if (isOK)
            {
                _beeper.PlayTone(OkFreq, OkDurationMs, Volume);
            }
            else
            {
                for (int i = 0; i < NgBeepCount; i++)
                    _beeper.PlayTone(ngFreq, NgDurationMs, Volume);
            }
        }

        /// <summary>报警发声：3 声高频。</summary>
        public void ReportAlarm()
        {
            if (Muted) return;
            for (int i = 0; i < AlarmBeepCount; i++)
                _beeper.PlayTone(AlarmFreq, AlarmDurationMs, Volume);
        }

        /// <summary>复位连续 NG 计数（报警复位 / 下班时调用）。</summary>
        public void Reset()
        {
            lock (_gate) _consecutiveNg = 0;
        }

        /// <summary>应用系统设置中的静音/音量。</summary>
        public void ApplySettings(bool muted, int volume)
        {
            Muted = muted;
            Volume = Math.Max(0, Math.Min(100, volume));
        }

        public void Dispose()
        {
            // 蜂鸣器由 DI 单例管理生命周期；此处无额外资源。
        }
    }
}
