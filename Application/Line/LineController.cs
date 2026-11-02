using System;
using TripleDetection.Application.Services;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 产线状态机实现。策略参数由构造注入（来自 SystemConfigService）：
    /// ConsecutiveNgToStopLine（默认 10）、OnPlcDisconnect（StopLine | ManualMode，默认 StopLine）。
    /// 连续超时阈值固定为 3（设计文档 §3.3.3）。
    /// </summary>
    public class LineController : ILineController
    {
        private const int ConsecutiveTimeoutToAlarm = 3;

        private readonly object _gate = new object();
        private readonly LoggingService _logService;
        private readonly int _consecutiveNgToStopLine;
        private readonly bool _plcDisconnectStopsLine;

        private LineState _state = LineState.Idle;
        private int _alarmCode;
        private string _alarmMessage;
        private int _consecutiveNg;
        private int _consecutiveTimeout;
        private bool _pausedByPlcDisconnect;

        public LineController(LoggingService logService, int consecutiveNgToStopLine, string onPlcDisconnect)
        {
            _logService = logService;
            _consecutiveNgToStopLine = consecutiveNgToStopLine > 0 ? consecutiveNgToStopLine : 10;
            _plcDisconnectStopsLine = !string.Equals(onPlcDisconnect, "ManualMode", StringComparison.OrdinalIgnoreCase);
        }

        public LineState State { get { lock (_gate) return _state; } }
        public int AlarmCode { get { lock (_gate) return _alarmCode; } }
        public string AlarmMessage { get { lock (_gate) return _alarmMessage; } }

        public event EventHandler<LineStateChangedEventArgs> StateChanged;
        public event EventHandler<LineAlarmEventArgs> AlarmRaised;

        public void StartShift() => Transition(LineState.Idle, LineState.Ready, "开班");

        public void StartAutoRun()
        {
            Transition(LineState.Ready, LineState.Running, "启动自动运行");
            Transition(LineState.Paused, LineState.Running, "恢复自动运行");
        }

        public void StopAutoRun() => Transition(LineState.Running, LineState.Ready, "停止自动运行");

        public void Pause() => Transition(LineState.Running, LineState.Paused, "暂停");

        public void ResetAlarm()
        {
            lock (_gate)
            {
                if (_state != LineState.Alarm) return;
            }
            Transition(LineState.Alarm, LineState.Ready, "报警复位");
            lock (_gate)
            {
                _alarmCode = 0;
                _alarmMessage = null;
                _consecutiveNg = 0;
                _consecutiveTimeout = 0;
                _pausedByPlcDisconnect = false;
            }
            _logService?.LogInfo("产线", "报警已复位，产线回到 Ready");
        }

        public void EndShift()
        {
            lock (_gate)
            {
                if (_state == LineState.Idle) return;
            }
            Transition(LineState.Idle, LineState.Idle, "下班");
            lock (_gate)
            {
                _alarmCode = 0;
                _alarmMessage = null;
                _consecutiveNg = 0;
                _consecutiveTimeout = 0;
                _pausedByPlcDisconnect = false;
            }
        }

        public void OnDetectionResult(bool isOK, bool isTimeout)
        {
            LineAlarmEventArgs alarm = null;
            lock (_gate)
            {
                if (_state != LineState.Running) return;
                if (isOK)
                {
                    _consecutiveNg = 0;
                    _consecutiveTimeout = 0;
                    return;
                }
                if (isTimeout)
                {
                    _consecutiveTimeout++;
                    if (_consecutiveTimeout >= ConsecutiveTimeoutToAlarm)
                        alarm = RaiseAlarmLocked(LineAlarmCode.ConsecutiveTimeout, $"连续检测超时 {_consecutiveTimeout} 次");
                }
                else
                {
                    _consecutiveNg++;
                    if (_consecutiveNg >= _consecutiveNgToStopLine)
                        alarm = RaiseAlarmLocked(LineAlarmCode.ConsecutiveNg, $"连续 NG {_consecutiveNg} 次，达到阈值 {_consecutiveNgToStopLine}");
                }
            }
            AlarmRaised?.Invoke(this, alarm);
        }

        public void OnPlcDisconnected()
        {
            LineAlarmEventArgs alarm = null;
            LineStateChangedEventArgs stateChange = null;
            lock (_gate)
            {
                if (_plcDisconnectStopsLine)
                {
                    if (_state != LineState.Alarm)
                        alarm = RaiseAlarmLocked(LineAlarmCode.PlcDisconnected, "PLC 断线（StopLine 策略）");
                }
                else if (_state == LineState.Running)
                {
                    _state = LineState.Paused;
                    _pausedByPlcDisconnect = true;
                    stateChange = new LineStateChangedEventArgs(LineState.Running, LineState.Paused);
                }
            }
            if (stateChange != null) StateChanged?.Invoke(this, stateChange);
            AlarmRaised?.Invoke(this, alarm);
            _logService?.LogWarn("产线", $"PLC 断线：策略 {(_plcDisconnectStopsLine ? "StopLine" : "ManualMode")}，当前状态 {State}");
        }

        public void OnPlcReconnected()
        {
            lock (_gate)
            {
                _pausedByPlcDisconnect = false;
            }
            _logService?.LogInfo("产线", "PLC 重连成功");
        }

        public void RaiseAlarm(int alarmCode, string message)
        {
            LineAlarmEventArgs alarm = null;
            lock (_gate)
            {
                alarm = RaiseAlarmLocked(alarmCode, message);
            }
            AlarmRaised?.Invoke(this, alarm);
        }

        // 调用方需持有 _gate
        private LineAlarmEventArgs RaiseAlarmLocked(int alarmCode, string message)
        {
            if (_state == LineState.Alarm) return null; // 已有活动报警，不覆盖
            _state = LineState.Alarm;
            _alarmCode = alarmCode;
            _alarmMessage = message;
            _logService?.LogError("产线", $"报警 [{alarmCode}] {message}");
            return new LineAlarmEventArgs(alarmCode, message);
        }

        // 从 fromState 转移到 toState（EndShift 时 fromState 为占位 Idle，实际允许任意状态）
        private void Transition(LineState fromState, LineState toState, string action)
        {
            LineStateChangedEventArgs args = null;
            lock (_gate)
            {
                bool allowed = toState == LineState.Idle
                    ? _state != LineState.Idle
                    : _state == fromState && _state != toState;
                if (!allowed) return;
                var from = _state;
                _state = toState;
                args = new LineStateChangedEventArgs(from, toState);
            }
            StateChanged?.Invoke(this, args);
            _logService?.LogInfo("产线", $"{action}：{args.From} → {args.To}");
        }
    }
}
