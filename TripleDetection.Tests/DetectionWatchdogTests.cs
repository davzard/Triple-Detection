using System;
using System.Threading;
using TripleDetection.Application.Line;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>检测看门狗：超时触发 / 提前停止 / 重置。</summary>
    public class DetectionWatchdogTests
    {
        [Fact]
        public void Start_FiresTimeout_AfterElapsed()
        {
            using var wd = new DetectionWatchdog();
            var fired = new ManualResetEventSlim(false);
            wd.Timeout += (s, e) => fired.Set();

            wd.Start(100);

            Assert.True(fired.Wait(2000), "看门狗应在超时后触发");
        }

        [Fact]
        public void Stop_BeforeTimeout_DoesNotFire()
        {
            using var wd = new DetectionWatchdog();
            var fired = new ManualResetEventSlim(false);
            wd.Timeout += (s, e) => fired.Set();

            wd.Start(200);
            Thread.Sleep(50);
            wd.Stop();

            Assert.False(fired.Wait(500), "停止后不应触发");
        }

        [Fact]
        public void Start_NonPositive_Disabled()
        {
            using var wd = new DetectionWatchdog();
            var fired = new ManualResetEventSlim(false);
            wd.Timeout += (s, e) => fired.Set();

            wd.Start(0);

            Assert.False(fired.Wait(300), "timeoutMs<=0 应禁用看门狗");
            Assert.False(wd.IsRunning);
        }

        [Fact]
        public void Start_Again_ResetsTimer()
        {
            using var wd = new DetectionWatchdog();
            int count = 0;
            wd.Timeout += (s, e) => Interlocked.Increment(ref count);

            wd.Start(150);
            Thread.Sleep(100);
            wd.Start(150); // 重置：距上次 Start 重新计 150ms
            Thread.Sleep(100); // 距第一次 Start 已 200ms，但重置后只过了 100ms

            Assert.Equal(0, Volatile.Read(ref count)); // 重置后不应立即触发
            Assert.True(WaitFor(() => Volatile.Read(ref count) == 1, 2000), "重置后应再触发一次");
        }

        [Fact]
        public void IsRunning_ReflectsState()
        {
            using var wd = new DetectionWatchdog();
            Assert.False(wd.IsRunning);
            wd.Start(500);
            Assert.True(wd.IsRunning);
            wd.Stop();
            Assert.False(wd.IsRunning);
        }

        private static bool WaitFor(Func<bool> cond, int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (cond()) return true;
                Thread.Sleep(20);
            }
            return cond();
        }
    }
}
