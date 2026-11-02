using System.Collections.Generic;
using TripleDetection.Application.Line;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>M8 HMI 声音服务：OK/NG/报警音序、连续 NG 音调升级、静音与音量。</summary>
    public class HmiSoundServiceTests
    {
        private sealed class FakeBeeper : IAudioBeeper
        {
            public readonly List<(int Freq, int Ms, int Vol)> Tones = new List<(int, int, int)>();
            public void PlayTone(int frequencyHz, int durationMs, int volumePercent)
                => Tones.Add((frequencyHz, durationMs, volumePercent));
        }

        private static (HmiSoundService Svc, FakeBeeper Beeper) Create()
        {
            var beeper = new FakeBeeper();
            return (new HmiSoundService(beeper), beeper);
        }

        [Fact]
        public void Ok_SingleShortTone_1000Hz_150ms()
        {
            var (svc, beeper) = Create();
            svc.ReportResult(true);
            Assert.Single(beeper.Tones);
            Assert.Equal((1000, 150, 80), beeper.Tones[0]);
        }

        [Fact]
        public void Ng_First_TwoLongTones_BasePitch600()
        {
            var (svc, beeper) = Create();
            svc.ReportResult(false);
            Assert.Equal(2, beeper.Tones.Count);
            Assert.All(beeper.Tones, t => Assert.Equal((600, 400, 80), t));
            Assert.Equal(1, svc.ConsecutiveNg);
        }

        [Fact]
        public void Ng_Consecutive_PitchEscalates_100HzPerStep()
        {
            var (svc, beeper) = Create();
            svc.ReportResult(false); // 600
            Assert.Equal(600, beeper.Tones[0].Freq);

            svc.ReportResult(false); // 700
            Assert.Equal(700, beeper.Tones[2].Freq);

            svc.ReportResult(false); // 800
            Assert.Equal(800, beeper.Tones[4].Freq);
            Assert.Equal(3, svc.ConsecutiveNg);
        }

        [Fact]
        public void Ng_EscalationCapsAtFiveLevels_1100Hz()
        {
            var (svc, beeper) = Create();
            for (int i = 0; i < 8; i++) svc.ReportResult(false);
            // 第 8 次 NG：level = min(7, 5) = 5 → 1100Hz
            var last = beeper.Tones[^1];
            Assert.Equal(1100, last.Freq);
            Assert.Equal(8, svc.ConsecutiveNg);
        }

        [Fact]
        public void Ok_AfterNg_ResetsCounterAndNextNgUsesBasePitch()
        {
            var (svc, beeper) = Create();
            svc.ReportResult(false);
            svc.ReportResult(false);
            svc.ReportResult(true); // OK 清零
            Assert.Equal(0, svc.ConsecutiveNg);

            svc.ReportResult(false);
            Assert.Equal(600, beeper.Tones[^1].Freq);
        }

        [Fact]
        public void Muted_NoTonesButCounterStillCounts()
        {
            var (svc, beeper) = Create();
            svc.Muted = true;
            svc.ReportResult(false);
            svc.ReportResult(false);
            Assert.Empty(beeper.Tones);
            Assert.Equal(2, svc.ConsecutiveNg);

            svc.Muted = false;
            svc.ReportResult(false);
            Assert.Equal(800, beeper.Tones[0].Freq); // 静音期间计数保留，音调继续升级
        }

        [Fact]
        public void Alarm_ThreeHighPitchTones_1500Hz_300ms()
        {
            var (svc, beeper) = Create();
            svc.ReportAlarm();
            Assert.Equal(3, beeper.Tones.Count);
            Assert.All(beeper.Tones, t => Assert.Equal((1500, 300, 80), t));
        }

        [Fact]
        public void Alarm_Muted_NoTones()
        {
            var (svc, beeper) = Create();
            svc.Muted = true;
            svc.ReportAlarm();
            Assert.Empty(beeper.Tones);
        }

        [Fact]
        public void ApplySettings_ClammpsVolumeTo0_100()
        {
            var (svc, _) = Create();
            svc.ApplySettings(false, 150);
            Assert.Equal(100, svc.Volume);
            svc.ApplySettings(false, -20);
            Assert.Equal(0, svc.Volume);
            svc.ApplySettings(true, 55);
            Assert.True(svc.Muted);
            Assert.Equal(55, svc.Volume);
        }

        [Fact]
        public void Volume_IsPassedToBeeper()
        {
            var (svc, beeper) = Create();
            svc.ApplySettings(false, 30);
            svc.ReportResult(true);
            Assert.Equal(30, beeper.Tones[0].Vol);
        }

        [Fact]
        public void Reset_ClearsConsecutiveNg()
        {
            var (svc, beeper) = Create();
            svc.ReportResult(false);
            svc.ReportResult(false);
            svc.Reset();
            Assert.Equal(0, svc.ConsecutiveNg);
            svc.ReportResult(false);
            Assert.Equal(600, beeper.Tones[^1].Freq);
        }
    }
}
