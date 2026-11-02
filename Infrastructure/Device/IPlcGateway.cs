using System;
using System.Threading;
using System.Threading.Tasks;

namespace TripleDetection.Infrastructure.Device
{
    /// <summary>PLC 触发事件参数（上升沿触发，已做抖动过滤）</summary>
    public class PlcTriggerEventArgs : EventArgs
    {
        public int TriggerValue { get; }
        public PlcTriggerEventArgs(int triggerValue) { TriggerValue = triggerValue; }
    }

    /// <summary>PLC 配方变更事件参数</summary>
    public class RecipeChangeEventArgs : EventArgs
    {
        public int RecipeId { get; }
        public RecipeChangeEventArgs(int recipeId) { RecipeId = recipeId; }
    }

    /// <summary>检测结果判定（写入 PLC RESULT 寄存器：0 空闲 / 1 OK / 2 NG）</summary>
    public enum DetectionVerdict { Idle = 0, Ok = 1, Ng = 2 }

    /// <summary>
    /// PLC 通信网关抽象：隔离产线集成与具体 PLC 协议（Modbus TCP）。
    /// Mock 模式用于无 PLC 环境；ModbusTcp 模式用于实际产线。
    /// 所有事件在非 UI 线程触发，订阅方需自行切回 UI 线程。
    /// </summary>
    public interface IPlcGateway
    {
        /// <summary>PLC 是否已连接</summary>
        bool IsConnected { get; }

        /// <summary>触发信号上升沿（已做抖动过滤）</summary>
        event EventHandler<PlcTriggerEventArgs> TriggerReceived;

        /// <summary>配方变更信号上升沿</summary>
        event EventHandler<RecipeChangeEventArgs> RecipeChanged;

        /// <summary>PLC 断开连接</summary>
        event EventHandler PlcDisconnected;

        /// <summary>PLC 重新连接成功</summary>
        event EventHandler PlcReconnected;

        /// <summary>连接 PLC（ModbusTcp 模式内部启动轮询与重连循环）</summary>
        Task ConnectAsync(CancellationToken ct = default);

        /// <summary>断开 PLC</summary>
        Task DisconnectAsync();

        /// <summary>写检测结果（RESULT 寄存器）</summary>
        Task WriteResultAsync(DetectionVerdict verdict, CancellationToken ct = default);

        /// <summary>写剔除命令（REJECT_CMD 脉冲 + 延迟/持续参数，PLC 侧控制剔除机构）</summary>
        Task WriteRejectCommandAsync(int delayMs, int durationMs, CancellationToken ct = default);

        /// <summary>写产线停机命令（LINE_STOP + ALARM_CODE）</summary>
        Task WriteLineStopAsync(int alarmCode, CancellationToken ct = default);

        /// <summary>写安灯报警码（ALARM_CODE 寄存器，电气侧显示；0 = 清除报警）</summary>
        Task WriteAlarmCodeAsync(int alarmCode, CancellationToken ct = default);

        /// <summary>写工位状态（STATION_STATUS：0 空闲 / 1 运行 / 2 报警）</summary>
        Task WriteStationStatusAsync(int status, CancellationToken ct = default);
    }
}
