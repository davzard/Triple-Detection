using System;
using System.Threading;
using TripleDetection.Application.Services;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 检测节拍监控（设计文档 §3.3.4）：滚动窗口统计"触发到结果"耗时，
    /// P99 &gt; 2× 均值时日志告警（性能退化预警，不触发 Alarm）。
    /// </summary>
    public class CycleTimeMonitor
    {
        private const int WindowSize = 200;
        private const int MinSamples = 20;
        private const int WarnCooldownMs = 60000;

        private readonly LoggingService _logService;
        private readonly object _gate = new object();
        private readonly Queue<long> _samples = new Queue<long>();
        private int _lastWarnAtTicks;

        public CycleTimeMonitor(LoggingService logService)
        {
            _logService = logService;
        }

        /// <summary>当前窗口统计（UI 显示用）。</summary>
        public (int SampleCount, long AverageMs, long P99Ms) GetStats()
        {
            lock (_gate)
            {
                if (_samples.Count == 0) return (0, 0, 0);
                var arr = _samples.ToArray();
                long sum = 0;
                foreach (var v in arr) sum += v;
                long avg = sum / arr.Length;
                Array.Sort(arr);
                int p99Index = (int)Math.Ceiling(arr.Length * 0.99) - 1;
                if (p99Index < 0) p99Index = 0;
                if (p99Index >= arr.Length) p99Index = arr.Length - 1;
                return (arr.Length, avg, arr[p99Index]);
            }
        }

        /// <summary>记录一次检测耗时（ms）。线程安全。</summary>
        public void Record(long elapsedMs)
        {
            if (elapsedMs < 0) return;
            lock (_gate)
            {
                _samples.Enqueue(elapsedMs);
                while (_samples.Count > WindowSize) _samples.Dequeue();
                if (_samples.Count < MinSamples) return;

                var arr = _samples.ToArray();
                long sum = 0;
                foreach (var v in arr) sum += v;
                long avg = sum / arr.Length;
                if (avg <= 0) return;

                Array.Sort(arr);
                int p99Index = (int)Math.Ceiling(arr.Length * 0.99) - 1;
                if (p99Index < 0) p99Index = 0;
                if (p99Index >= arr.Length) p99Index = arr.Length - 1;
                long p99 = arr[p99Index];

                if (p99 > 2 * avg)
                {
                    var now = Environment.TickCount;
                    if (now - _lastWarnAtTicks >= WarnCooldownMs)
                    {
                        _lastWarnAtTicks = now;
                        _logService?.LogWarn("产线",
                            $"检测节拍性能退化预警：P99={p99}ms > 2×均值（均值={avg}ms, 样本={arr.Length}）");
                    }
                }
            }
        }
    }
}
