using System;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 产线状态机（与 PLC 协议无关）：
    /// Idle → Ready → Running ⇄ Paused；Running → Alarm（连续 NG / 超时 / PLC 断线 StopLine）；
    /// Alarm → Ready（操作员复位）；任意状态 → Idle（下班）。
    /// </summary>
    public interface ILineController
    {
        LineState State { get; }
        int AlarmCode { get; }
        string AlarmMessage { get; }

        event EventHandler<LineStateChangedEventArgs> StateChanged;
        event EventHandler<LineAlarmEventArgs> AlarmRaised;

        /// <summary>开班：Idle → Ready</summary>
        void StartShift();
        /// <summary>启动自动运行：Ready/Paused → Running</summary>
        void StartAutoRun();
        /// <summary>停止自动运行：Running → Ready（免鼠标 F1；班次保持）</summary>
        void StopAutoRun();
        /// <summary>暂停：Running → Paused</summary>
        void Pause();
        /// <summary>报警复位：Alarm → Ready</summary>
        void ResetAlarm();
        /// <summary>下班：任意 → Idle</summary>
        void EndShift();

        /// <summary>检测结果反馈（仅 Running 状态计数）：isTimeout 为看门狗超时合成的 NG</summary>
        void OnDetectionResult(bool isOK, bool isTimeout);
        /// <summary>PLC 断线：按策略停机（StopLine）或转手动（ManualMode）</summary>
        void OnPlcDisconnected();
        /// <summary>PLC 重连</summary>
        void OnPlcReconnected();
        /// <summary>外部报警（相机心跳丢失 / 方案崩溃等）</summary>
        void RaiseAlarm(int alarmCode, string message);
    }
}
