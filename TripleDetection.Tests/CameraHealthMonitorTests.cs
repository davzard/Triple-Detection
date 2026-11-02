using System;
using System.IO;
using System.Threading;
using TripleDetection.Application.Line;
using TripleDetection.Application.Services;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>
    /// 相机心跳监控测试（Phase 2，设计文档 §3.3.4）：
    /// Running 期间心跳超时 → Alarm 2001；Idle / 从未出图 / 心跳正常 → 不报警。
    /// </summary>
    public class CameraHealthMonitorTests : IDisposable
    {
        private readonly string _dir;

        public CameraHealthMonitorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "td_cam_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private class FakeHeartbeat : ICameraHeartbeat
        {
            public DateTime? LastFrameTime { get; set; }
        }

        private static (CameraHealthMonitor monitor, LineController line, FakeHeartbeat heartbeat) Create(int timeoutMs)
        {
            var log = new LoggingService(Path.Combine(Path.GetTempPath(), "td_cam_logs"), null);
            var heartbeat = new FakeHeartbeat();
            var line = new LineController(log, 3, "StopLine");
            var monitor = new CameraHealthMonitor(heartbeat, line, log, timeoutMs);
            return (monitor, line, heartbeat);
        }

        private static bool WaitFor(Func<bool> cond, int ms = 4000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (cond()) return true;
                Thread.Sleep(50);
            }
            return cond();
        }

        [Fact]
        public void Running_心跳超时_触发Alarm2001()
        {
            var (monitor, line, heartbeat) = Create(timeoutMs: 300);
            heartbeat.LastFrameTime = DateTime.Now.AddSeconds(-10); // 10s 前出图
            line.StartShift();
            line.StartAutoRun();
            Assert.Equal(LineState.Running, line.State);

            monitor.Start();
            Assert.True(WaitFor(() => line.State == LineState.Alarm), "心跳超时应触发 Alarm");
            Assert.Equal((int)LineAlarmCode.CameraHeartbeatLost, line.AlarmCode);
            monitor.Dispose();
        }

        [Fact]
        public void Idle_心跳超时_不报警()
        {
            var (monitor, line, heartbeat) = Create(timeoutMs: 300);
            heartbeat.LastFrameTime = DateTime.Now.AddSeconds(-10);
            // 未开班（Idle）

            monitor.Start();
            Thread.Sleep(1500);
            Assert.Equal(LineState.Idle, line.State);
            Assert.Equal(0, line.AlarmCode);
            monitor.Dispose();
        }

        [Fact]
        public void 从未出图_不报警()
        {
            var (monitor, line, heartbeat) = Create(timeoutMs: 300);
            heartbeat.LastFrameTime = null; // 从未出图
            line.StartShift();
            line.StartAutoRun();

            monitor.Start();
            Thread.Sleep(1500);
            Assert.Equal(LineState.Running, line.State);
            Assert.Equal(0, line.AlarmCode);
            monitor.Dispose();
        }

        [Fact]
        public void 心跳正常_不报警()
        {
            var (monitor, line, heartbeat) = Create(timeoutMs: 300);
            heartbeat.LastFrameTime = DateTime.Now;
            var refresher = new Timer(_ => heartbeat.LastFrameTime = DateTime.Now, null, 0, 200);
            line.StartShift();
            line.StartAutoRun();

            monitor.Start();
            Thread.Sleep(1500);
            Assert.Equal(LineState.Running, line.State);
            Assert.Equal(0, line.AlarmCode);
            monitor.Dispose();
            refresher.Dispose();
        }
    }
}
