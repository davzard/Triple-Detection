using System;
using System.Threading;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 检测看门狗：每次触发启动一次超时计时，检测完成前未停止则触发 Timeout 事件。
    /// 单次触发（Start 会重置之前的计时），线程安全。
    /// </summary>
    public class DetectionWatchdog : IDisposable
    {
        private readonly Timer _timer;
        private int _running;

        public event EventHandler Timeout;

        public DetectionWatchdog()
        {
            _timer = new Timer(_ =>
            {
                if (Interlocked.Exchange(ref _running, 0) == 1)
                    Timeout?.Invoke(this, EventArgs.Empty);
            });
        }

        /// <summary>启动（重置）超时计时；timeoutMs &lt;= 0 视为不启用</summary>
        public void Start(int timeoutMs)
        {
            if (timeoutMs <= 0) return;
            Interlocked.Exchange(ref _running, 1);
            _timer.Change(timeoutMs, System.Threading.Timeout.Infinite);
        }

        /// <summary>停止计时（检测完成）</summary>
        public void Stop()
        {
            Interlocked.Exchange(ref _running, 0);
            _timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }

        public bool IsRunning => Volatile.Read(ref _running) == 1;

        public void Dispose() => _timer.Dispose();
    }
}
