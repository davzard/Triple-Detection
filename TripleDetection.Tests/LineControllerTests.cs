using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TripleDetection.Application.Line;
using TripleDetection.Application.Services;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>产线状态机：全转换 + 连续 NG/超时/PLC 断线策略。</summary>
    public class LineControllerTests : IDisposable
    {
        private readonly string _dir;
        private readonly LoggingService _log;

        public LineControllerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "td_line_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _log = new LoggingService(_dir, null);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static LineController Create(int ngThreshold = 3, string onPlcDisconnect = "StopLine")
            => new LineController(new LoggingService(Path.GetTempPath(), null), ngThreshold, onPlcDisconnect);

        [Fact]
        public void InitialState_IsIdle()
        {
            Assert.Equal(LineState.Idle, Create().State);
        }

        [Fact]
        public void StartShift_IdleToReady_RaisesStateChanged()
        {
            var lc = Create();
            LineStateChangedEventArgs args = null;
            lc.StateChanged += (s, e) => args = e;

            lc.StartShift();

            Assert.Equal(LineState.Ready, lc.State);
            Assert.NotNull(args);
            Assert.Equal(LineState.Idle, args.From);
            Assert.Equal(LineState.Ready, args.To);
        }

        [Fact]
        public void StartShift_FromNonIdle_Ignored()
        {
            var lc = Create();
            lc.StartShift();
            lc.StartShift(); // 重复开班无效
            Assert.Equal(LineState.Ready, lc.State);
        }

        [Fact]
        public void StartAutoRun_ReadyToRunning()
        {
            var lc = Create();
            lc.StartShift();
            lc.StartAutoRun();
            Assert.Equal(LineState.Running, lc.State);
        }

        [Fact]
        public void StartAutoRun_FromIdle_Ignored()
        {
            var lc = Create();
            lc.StartAutoRun();
            Assert.Equal(LineState.Idle, lc.State);
        }

        [Fact]
        public void Pause_RunningToPaused_ThenResume()
        {
            var lc = Create();
            lc.StartShift();
            lc.StartAutoRun();
            lc.Pause();
            Assert.Equal(LineState.Paused, lc.State);
            lc.StartAutoRun();
            Assert.Equal(LineState.Running, lc.State);
        }

        [Fact]
        public void StopAutoRun_RunningToReady_RaisesStateChanged()
        {
            var lc = Create();
            lc.StartShift();
            lc.StartAutoRun();
            LineStateChangedEventArgs args = null;
            lc.StateChanged += (s, e) => args = e;

            lc.StopAutoRun();

            Assert.Equal(LineState.Ready, lc.State);
            Assert.NotNull(args);
            Assert.Equal(LineState.Running, args.From);
            Assert.Equal(LineState.Ready, args.To);
        }

        [Fact]
        public void StopAutoRun_FromNonRunning_Ignored()
        {
            var lc = Create();
            lc.StopAutoRun(); // Idle 无效
            Assert.Equal(LineState.Idle, lc.State);
            lc.StartShift();
            lc.StopAutoRun(); // Ready 无效
            Assert.Equal(LineState.Ready, lc.State);
            lc.StartAutoRun();
            lc.Pause();
            lc.StopAutoRun(); // Paused 无效
            Assert.Equal(LineState.Paused, lc.State);
        }

        [Fact]
        public void ConsecutiveNg_ReachThreshold_RaisesAlarm1001()
        {
            var lc = Create(ngThreshold: 3);
            lc.StartShift();
            lc.StartAutoRun();
            LineAlarmEventArgs alarm = null;
            lc.AlarmRaised += (s, e) => alarm = e;

            lc.OnDetectionResult(false, false);
            lc.OnDetectionResult(false, false);
            Assert.Equal(LineState.Running, lc.State);
            Assert.Null(alarm);

            lc.OnDetectionResult(false, false);
            Assert.Equal(LineState.Alarm, lc.State);
            Assert.NotNull(alarm);
            Assert.Equal(LineAlarmCode.ConsecutiveNg, alarm.AlarmCode);
            Assert.Equal(LineAlarmCode.ConsecutiveNg, lc.AlarmCode);
        }

        [Fact]
        public void Ok_ResetsConsecutiveNgCounter()
        {
            var lc = Create(ngThreshold: 3);
            lc.StartShift();
            lc.StartAutoRun();

            lc.OnDetectionResult(false, false);
            lc.OnDetectionResult(false, false);
            lc.OnDetectionResult(true, false); // OK 清零
            lc.OnDetectionResult(false, false);
            lc.OnDetectionResult(false, false);
            Assert.Equal(LineState.Running, lc.State); // 未达阈值
        }

        [Fact]
        public void ConsecutiveTimeout_3Times_RaisesAlarm4001()
        {
            var lc = Create();
            lc.StartShift();
            lc.StartAutoRun();
            LineAlarmEventArgs alarm = null;
            lc.AlarmRaised += (s, e) => alarm = e;

            lc.OnDetectionResult(false, true);
            lc.OnDetectionResult(false, true);
            Assert.Equal(LineState.Running, lc.State);

            lc.OnDetectionResult(false, true);
            Assert.Equal(LineState.Alarm, lc.State);
            Assert.NotNull(alarm);
            Assert.Equal(LineAlarmCode.ConsecutiveTimeout, alarm.AlarmCode);
        }

        [Fact]
        public void DetectionResult_WhenNotRunning_Ignored()
        {
            var lc = Create(ngThreshold: 1);
            lc.StartShift(); // Ready，未运行
            lc.OnDetectionResult(false, false);
            Assert.Equal(LineState.Ready, lc.State);
            Assert.Equal(0, lc.AlarmCode);
        }

        [Fact]
        public void ResetAlarm_AlarmToReady_ClearsAlarm()
        {
            var lc = Create(ngThreshold: 1);
            lc.StartShift();
            lc.StartAutoRun();
            lc.OnDetectionResult(false, false);
            Assert.Equal(LineState.Alarm, lc.State);

            lc.ResetAlarm();
            Assert.Equal(LineState.Ready, lc.State);
            Assert.Equal(0, lc.AlarmCode);
            Assert.Null(lc.AlarmMessage);
        }

        [Fact]
        public void EndShift_FromAnyState_GoToIdle()
        {
            foreach (var setup in new[] { "idle", "ready", "running", "paused", "alarm" })
            {
                var lc = Create(ngThreshold: 1);
                switch (setup)
                {
                    case "ready": lc.StartShift(); break;
                    case "running": lc.StartShift(); lc.StartAutoRun(); break;
                    case "paused": lc.StartShift(); lc.StartAutoRun(); lc.Pause(); break;
                    case "alarm": lc.StartShift(); lc.StartAutoRun(); lc.OnDetectionResult(false, false); break;
                }
                lc.EndShift();
                Assert.Equal(LineState.Idle, lc.State);
            }
        }

        [Fact]
        public void PlcDisconnected_StopLinePolicy_RunningToAlarm3001()
        {
            var lc = Create(onPlcDisconnect: "StopLine");
            lc.StartShift();
            lc.StartAutoRun();
            LineAlarmEventArgs alarm = null;
            lc.AlarmRaised += (s, e) => alarm = e;

            lc.OnPlcDisconnected();

            Assert.Equal(LineState.Alarm, lc.State);
            Assert.NotNull(alarm);
            Assert.Equal(LineAlarmCode.PlcDisconnected, alarm.AlarmCode);
        }

        [Fact]
        public void PlcDisconnected_ManualModePolicy_RunningToPaused()
        {
            var lc = Create(onPlcDisconnect: "ManualMode");
            lc.StartShift();
            lc.StartAutoRun();
            LineAlarmEventArgs alarm = null;
            lc.AlarmRaised += (s, e) => alarm = e;

            lc.OnPlcDisconnected();

            Assert.Equal(LineState.Paused, lc.State);
            Assert.Null(alarm);
        }

        [Fact]
        public void RaiseAlarm_WhenAlreadyInAlarm_DoesNotOverwrite()
        {
            var lc = Create(ngThreshold: 1);
            lc.StartShift();
            lc.StartAutoRun();
            lc.OnDetectionResult(false, false);
            Assert.Equal(LineAlarmCode.ConsecutiveNg, lc.AlarmCode);

            lc.RaiseAlarm(LineAlarmCode.CameraHeartbeatLost, "相机丢失");
            Assert.Equal(LineAlarmCode.ConsecutiveNg, lc.AlarmCode); // 原报警保留
        }
    }
}
