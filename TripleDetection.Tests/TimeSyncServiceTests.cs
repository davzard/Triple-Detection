using System;
using TripleDetection.Application.Services;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>M9-lite NTP 时间同步：时间戳解析 + 偏差公式。</summary>
    public class TimeSyncServiceTests
    {
        [Fact]
        public void NtpTimestampToUtc_WholeSeconds_From1900Epoch()
        {
            // 秒 = 367（0x0000016F），小数 = 0 → 1900-01-01T00:06:07Z
            var packet = new byte[48];
            packet[0] = 0x00; packet[1] = 0x00; packet[2] = 0x01; packet[3] = 0x6F;
            var t = TimeSyncService.NtpTimestampToUtc(packet, 0);
            Assert.Equal(new DateTime(1900, 1, 1, 0, 6, 7, DateTimeKind.Utc), t);
        }

        [Fact]
        public void NtpTimestampToUtc_Fraction_ScaledToTicks()
        {
            // 秒 = 1（大端：packet[3]），小数 = 0x80000000 = 0.5 → 1900-01-01T00:00:01.500Z
            var packet = new byte[48];
            packet[3] = 0x01;
            packet[4] = 0x80;
            var t = TimeSyncService.NtpTimestampToUtc(packet, 0);
            Assert.Equal(new DateTime(1900, 1, 1, 0, 0, 1, 500, DateTimeKind.Utc), t);
        }

        [Fact]
        public void NtpTimestampToUtc_ZeroSeconds_ReturnsDefault()
        {
            var packet = new byte[48];
            Assert.Equal(default(DateTime), TimeSyncService.NtpTimestampToUtc(packet, 0));
        }

        [Fact]
        public void ComputeOffset_ServerAhead_ReturnsPositiveOffset()
        {
            var t1 = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            var t2 = t1.AddMilliseconds(100); // 服务器接收：服务器时钟领先 100ms
            var t3 = t2.AddMilliseconds(10);  // 服务器发送（处理 10ms）
            var t4 = t1.AddMilliseconds(20);  // 本机接收（往返 20ms）
            // offset = ((t2-t1) + (t3-t4)) / 2 = (100 + 90) / 2 = 95ms
            var offset = TimeSyncService.ComputeOffset(t1, t2, t3, t4);
            Assert.Equal(95, (int)offset.TotalMilliseconds);
        }

        [Fact]
        public void ComputeOffset_ClocksInSync_ReturnsApproxZero()
        {
            var t1 = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            var t2 = t1.AddMilliseconds(10);
            var t3 = t2; // 服务器零处理
            var t4 = t1.AddMilliseconds(20);
            var offset = TimeSyncService.ComputeOffset(t1, t2, t3, t4);
            Assert.Equal(0, (int)offset.TotalMilliseconds);
        }
    }
}
