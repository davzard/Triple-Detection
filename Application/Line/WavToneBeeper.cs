using System;
using System.IO;
using System.Media;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 生产蜂鸣器：内存生成正弦波 WAV，用 SoundPlayer 播放（支持音量）。
    /// PlayTone 阻塞到播放结束，保证连续音序（NG 两声、报警三声）不重叠。
    /// </summary>
    public class WavToneBeeper : IAudioBeeper
    {
        private const int SampleRate = 44100;
        private const int FadeMs = 8; // 首尾淡入淡出，避免爆音

        public void PlayTone(int frequencyHz, int durationMs, int volumePercent)
        {
            if (durationMs <= 0) return;
            try
            {
                double volume = Math.Max(0, Math.Min(100, volumePercent)) / 100.0;
                var wav = BuildSineWav(frequencyHz, durationMs, volume);
                using (var ms = new MemoryStream(wav))
                using (var player = new SoundPlayer(ms))
                {
                    player.PlaySync();
                }
            }
            catch
            {
                // 无声卡/音频服务异常不阻塞产线
            }
        }

        /// <summary>生成 16-bit 单声道 WAV（44.1kHz），带淡入淡出包络。volume 0-1 直接缩放振幅。</summary>
        public static byte[] BuildSineWav(int frequencyHz, int durationMs, double volume = 1.0)
        {
            int totalSamples = SampleRate * durationMs / 1000;
            int fadeSamples = Math.Min(SampleRate * FadeMs / 1000, totalSamples / 2);
            double ampScale = Math.Max(0.0, Math.Min(1.0, volume)) * 16000;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                int dataSize = totalSamples * 2;
                // RIFF 头
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + dataSize);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                // fmt 块
                bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);            // fmt chunk size
                bw.Write((short)1);       // PCM
                bw.Write((short)1);       // mono
                bw.Write(SampleRate);
                bw.Write(SampleRate * 2); // byte rate
                bw.Write((short)2);       // block align
                bw.Write((short)16);      // bits per sample
                // data 块
                bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                bw.Write(dataSize);
                for (int i = 0; i < totalSamples; i++)
                {
                    double t = (double)i / SampleRate;
                    double amp = Math.Sin(2 * Math.PI * frequencyHz * t);
                    if (i < fadeSamples) amp *= (double)i / fadeSamples;
                    else if (i >= totalSamples - fadeSamples) amp *= (double)(totalSamples - i) / fadeSamples;
                    bw.Write((short)(amp * ampScale));
                }
                bw.Flush();
                return ms.ToArray();
            }
        }
    }
}
