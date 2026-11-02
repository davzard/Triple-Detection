using System;
using System.Collections.Generic;
using System.IO;
using TripleDetection.Application.Line;
using TripleDetection.Application.Services;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>
    /// 节拍监控测试（Phase 2，设计文档 §3.3.4）：
    /// P99 &gt; 2×均值 → 日志告警；平稳窗口 → 不告警；GetStats 返回窗口统计。
    /// </summary>
    public class CycleTimeMonitorTests : IDisposable
    {
        private readonly string _dir;

        public CycleTimeMonitorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "td_cycle_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private class CapturingLog : LoggingService
        {
            public readonly List<LogEntry> Entries = new List<LogEntry>();
            public CapturingLog(string dir) : base(dir, null)
            {
                OnLogAdded += (s, e) => { lock (Entries) Entries.Add(e); };
            }
            public bool HasWarnContaining(string fragment)
            {
                lock (Entries)
                    foreach (var e in Entries)
                        if (e.Level == "WARN" && e.Message.Contains(fragment)) return true;
                return false;
            }
        }

        [Fact]
        public void P99尖峰_触发告警()
        {
            var log = new CapturingLog(_dir);
            var monitor = new CycleTimeMonitor(log);

            for (int i = 0; i < 25; i++) monitor.Record(100);
            monitor.Record(5000); // 尖峰：P99=5000 > 2×均值

            Assert.True(log.HasWarnContaining("节拍性能退化"), "P99 尖峰应产生节拍告警日志");
        }

        [Fact]
        public void 平稳窗口_不告警()
        {
            var log = new CapturingLog(_dir);
            var monitor = new CycleTimeMonitor(log);

            for (int i = 0; i < 50; i++) monitor.Record(100 + (i % 5)); // 100~104ms 抖动

            Assert.False(log.HasWarnContaining("节拍性能退化"), "平稳窗口不应告警");
        }

        [Fact]
        public void 样本不足_不告警()
        {
            var log = new CapturingLog(_dir);
            var monitor = new CycleTimeMonitor(log);

            for (int i = 0; i < 10; i++) monitor.Record(100); // 少于最小样本数 20
            monitor.Record(99999);

            Assert.False(log.HasWarnContaining("节拍性能退化"), "样本不足时不应告警");
        }

        [Fact]
        public void GetStats_返回窗口统计()
        {
            var log = new CapturingLog(_dir);
            var monitor = new CycleTimeMonitor(log);

            Assert.Equal((0, 0, 0), monitor.GetStats());

            monitor.Record(100);
            monitor.Record(200);
            monitor.Record(300);
            var (count, avg, p99) = monitor.GetStats();
            Assert.Equal(3, count);
            Assert.Equal(200, avg);
            Assert.Equal(300, p99);
        }
    }
}
