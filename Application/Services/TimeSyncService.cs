using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TripleDetection.Application.Services
{
    /// <summary>
    /// 时间同步服务（设计文档 §3.9）：SNTP 查询 NTP 服务器获取本机时钟偏差。
    /// 偏差 > 5s 时开班前强制告警（追溯时间戳可信前提）。
    /// 网络不可达/超时时返回 null（不阻塞产线，仅记日志）。
    /// </summary>
    public interface ITimeSyncService
    {
        /// <summary>查询本机与 NTP 服务器的时间偏差（本机 - 服务器）。失败返回 null。</summary>
        TimeSpan? GetOffset(TimeSpan timeout);
    }

    /// <summary>
    /// SNTP 客户端（UDP 123，48 字节包，无第三方依赖）。
    /// 偏差公式：offset = ((T2 - T1) + (T3 - T4)) / 2，
    /// T1 本机发送、T2 服务器接收、T3 服务器发送、T4 本机接收。
    /// </summary>
    public class TimeSyncService : ITimeSyncService
    {
        private const int NtpPort = 123;
        private static readonly DateTime NtpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly string _host;

        public TimeSyncService(string host = "time.windows.com")
        {
            _host = string.IsNullOrWhiteSpace(host) ? "time.windows.com" : host;
        }

        public TimeSpan? GetOffset(TimeSpan timeout)
        {
            try
            {
                using (var client = new UdpClient())
                {
                    var request = new byte[48];
                    request[0] = 0x1B; // LI=0, VN=3, Mode=3 (client)
                    var t1 = DateTime.UtcNow;
                    client.Send(request, request.Length, _host, NtpPort);

                    var receiveAsync = client.ReceiveAsync();
                    if (!receiveAsync.Wait(timeout)) return null;
                    var t4 = DateTime.UtcNow;

                    var packet = receiveAsync.Result.Buffer;
                    if (packet.Length < 48) return null;

                    var t2 = NtpTimestampToUtc(packet, 24); // origin
                    var t3 = NtpTimestampToUtc(packet, 32); // transmit
                    if (t3 == default || t3 == NtpEpoch) return null;

                    return ComputeOffset(t1, t2, t3, t4);
                }
            }
            catch
            {
                return null; // 网络被防火墙拦截/无 DNS 等：不阻塞产线
            }
        }

        /// <summary>NTP 秒（1900 纪元，32 位整数 + 32 位小数）→ UTC DateTime。</summary>
        public static DateTime NtpTimestampToUtc(byte[] packet, int offset)
        {
            uint seconds = (uint)(packet[offset] << 24 | packet[offset + 1] << 16
                                 | packet[offset + 2] << 8 | packet[offset + 3]);
            uint fraction = (uint)(packet[offset + 4] << 24 | packet[offset + 5] << 16
                                  | packet[offset + 6] << 8 | packet[offset + 7]);
            if (seconds == 0) return default;
            var baseUtc = NtpEpoch.AddSeconds(seconds);
            var fractionTicks = (long)(fraction / 4294967296.0 * TimeSpan.TicksPerSecond);
            return baseUtc.AddTicks(fractionTicks);
        }

        /// <summary>经典 NTP 偏差公式。</summary>
        public static TimeSpan ComputeOffset(DateTime t1, DateTime t2, DateTime t3, DateTime t4)
        {
            var clockOffset = (t2 - t1) + (t3 - t4);
            return clockOffset / 2;
        }
    }
}
