using System;

namespace TripleDetection.Application.Line
{
    /// <summary>产线状态机状态</summary>
    public enum LineState
    {
        /// <summary>未开班（初始/下班后）</summary>
        Idle = 0,
        /// <summary>已开班待启动</summary>
        Ready = 1,
        /// <summary>自动运行中（响应 PLC 触发）</summary>
        Running = 2,
        /// <summary>暂停（手动暂停或 PLC 断线 ManualMode 策略）</summary>
        Paused = 3,
        /// <summary>报警（连续 NG / 超时 / PLC 断线 StopLine 策略等）</summary>
        Alarm = 4
    }

    /// <summary>产线报警码（写入 PLC ALARM_CODE 寄存器）</summary>
    public static class LineAlarmCode
    {
        /// <summary>连续 NG 达到阈值</summary>
        public const int ConsecutiveNg = 1001;
        /// <summary>相机心跳丢失</summary>
        public const int CameraHeartbeatLost = 2001;
        /// <summary>视觉方案崩溃</summary>
        public const int SolutionCrash = 2002;
        /// <summary>PLC 断线（StopLine 策略）</summary>
        public const int PlcDisconnected = 3001;
        /// <summary>连续检测超时</summary>
        public const int ConsecutiveTimeout = 4001;
        /// <summary>换型失败（RECIPE_ID 找不到产品/已审批任务）</summary>
        public const int RecipeNotFound = 5001;
    }

    /// <summary>状态变更事件参数</summary>
    public class LineStateChangedEventArgs : EventArgs
    {
        public LineState From { get; }
        public LineState To { get; }
        public LineStateChangedEventArgs(LineState from, LineState to)
        {
            From = from;
            To = to;
        }
    }

    /// <summary>报警事件参数</summary>
    public class LineAlarmEventArgs : EventArgs
    {
        public int AlarmCode { get; }
        public string Message { get; }
        public LineAlarmEventArgs(int alarmCode, string message)
        {
            AlarmCode = alarmCode;
            Message = message;
        }
    }
}
