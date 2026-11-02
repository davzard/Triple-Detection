using System;
using TripleDetection.Application.Line;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>WAV 蜂鸣器：文件结构 + 音量缩放。</summary>
    public class WavToneBeeperTests
    {
        private static short Peak(byte[] wav)
        {
            // 数据区从偏移 44 开始（16-bit 小端）
            short peak = 0;
            for (int i = 44; i + 1 < wav.Length; i += 2)
            {
                short s = (short)(wav[i] | (wav[i + 1] << 8));
                if (Math.Abs(s) > Math.Abs(peak)) peak = s;
            }
            return peak;
        }

        [Fact]
        public void BuildSineWav_RiffHeaderAndExpectedSize()
        {
            var wav = WavToneBeeper.BuildSineWav(1000, 100);
            int expected = 44 + 44100 * 100 / 1000 * 2;
            Assert.Equal(expected, wav.Length);
            Assert.Equal((byte)'R', wav[0]);
            Assert.Equal((byte)'I', wav[1]);
            Assert.Equal((byte)'F', wav[2]);
            Assert.Equal((byte)'F', wav[3]);
            Assert.Equal((byte)'W', wav[8]);
            Assert.Equal((byte)'A', wav[9]);
        }

        [Fact]
        public void BuildSineWav_VolumeScalesPeakAmplitude()
        {
            var full = WavToneBeeper.BuildSineWav(1000, 100, 1.0);
            var half = WavToneBeeper.BuildSineWav(1000, 100, 0.5);
            Assert.True(Math.Abs(Peak(half)) * 2 <= Math.Abs(Peak(full)) + 1);
        }

        [Fact]
        public void BuildSineWav_ZeroVolume_AllSilent()
        {
            var wav = WavToneBeeper.BuildSineWav(1000, 100, 0.0);
            Assert.Equal(0, Peak(wav));
        }
    }
}
