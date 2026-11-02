using System;
using System.Threading;
using TripleDetection.Application.Services;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 相机心跳监控（设计文档 §3.3.4）：产线 Running 期间，
    /// 若 <see cref="ICameraHeartbeat.LastFrameTime"/> 超过阈值未刷新，触发 Alarm 2001（相机离线）。
    /// 每秒检查一次；进入 Alarm 后停止重复报警（等待操作员复位）。
    /// </summary>
    public class CameraHealthMonitor : IDisposable
    {
        private readonly ICameraHeartbeat _heartbeat;
        private readonly ILineController _line;
        private readonly LoggingService _logService;
        private readonly int _timeoutMs;
        private readonly Timer _timer;
        private volatile bool _started;

        public CameraHealthMonitor(ICameraHeartbeat heartbeat, ILineController line, LoggingService logService, int timeoutMs = 30000)
        {
            _heartbeat = heartbeat ?? throw new ArgumentNullException(nameof(heartbeat));
            _line = line ?? throw new ArgumentNullException(nameof(line));
            _logService = logService;
            _timeoutMs = timeoutMs > 0 ? timeoutMs : 30000;
            _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>启动监控（登录成功后调用一次）。</summary>
        public void Start()
        {
            if (_started) return;
            _started = true;
            _timer.Change(1000, 1000);
            _logService?.LogInfo("产线", $"相机心跳监控已启动（阈值 {_timeoutMs}ms）");
        }

        /// <summary>停止监控（应用退出时调用）。</summary>
        public void Stop()
        {
            _started = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void OnTick(object state)
        {
            try
            {
                // 仅在 Running 期间检查（Alarm 后停止重复报警，等待复位）
                if (_line.State != LineState.Running) return;

                var last = _heartbeat.LastFrameTime;
                if (last == null) return; // 从未出图（未开班/方案未加载），不报警

                var ageMs = (DateTime.Now - last.Value).TotalMilliseconds;
                if (ageMs > _timeoutMs)
                {
                    var ageSec = Math.Round(ageMs / 1000.0, 1);
                    _logService?.LogError("产线", $"相机心跳丢失（{ageSec}s 无新帧 > {_timeoutMs / 1000.0}s），触发 Alarm 2001");
                    _line.RaiseAlarm(LineAlarmCode.CameraHeartbeatLost, $"相机心跳丢失：{ageSec}s 无新帧");
                }
            }
            catch (Exception ex)
            {
                _logService?.LogError("产线", $"相机心跳监控异常：{ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }
}
