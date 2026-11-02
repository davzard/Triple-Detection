using System;
using System.Threading;
using System.Threading.Tasks;
using TripleDetection.Application.Services;

namespace TripleDetection.Infrastructure.Device
{
    /// <summary>
    /// Mock PLC 网关：无 PLC 环境下的产线集成测试替身。
    /// 提供 Simulate* 方法模拟 PLC 行为，并记录 App 写入的寄存器值供断言。
    /// </summary>
    public class MockPlcGateway : IPlcGateway
    {
        private readonly LoggingService _logService;
        private volatile bool _connected;

        public MockPlcGateway(LoggingService logService)
        {
            _logService = logService;
        }

        public bool IsConnected => _connected;

        public event EventHandler<PlcTriggerEventArgs> TriggerReceived;
        public event EventHandler<RecipeChangeEventArgs> RecipeChanged;
        public event EventHandler PlcDisconnected;
        public event EventHandler PlcReconnected;

        // App → PLC 写入记录（供 UI 显示与测试断言）
        public volatile DetectionVerdict LastResult;
        public volatile int LastRejectDelayMs;
        public volatile int LastRejectDurationMs;
        public volatile int LastLineStopAlarmCode;
        public volatile int LastStationStatus;
        public volatile int TriggerCount;

        public Task ConnectAsync(CancellationToken ct = default)
        {
            _connected = true;
            _logService?.LogInfo("PLC", "Mock PLC 已连接");
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            _connected = false;
            _logService?.LogInfo("PLC", "Mock PLC 已断开");
            return Task.CompletedTask;
        }

        public Task WriteResultAsync(DetectionVerdict verdict, CancellationToken ct = default)
        {
            LastResult = verdict;
            return Task.CompletedTask;
        }

        public Task WriteRejectCommandAsync(int delayMs, int durationMs, CancellationToken ct = default)
        {
            LastRejectDelayMs = delayMs;
            LastRejectDurationMs = durationMs;
            _logService?.LogInfo("PLC", $"Mock PLC 收到剔除命令：延迟 {delayMs}ms，持续 {durationMs}ms");
            return Task.CompletedTask;
        }

        public Task WriteLineStopAsync(int alarmCode, CancellationToken ct = default)
        {
            LastLineStopAlarmCode = alarmCode;
            _logService?.LogWarn("PLC", $"Mock PLC 收到产线停机命令：报警码 {alarmCode}");
            return Task.CompletedTask;
        }

        public Task WriteStationStatusAsync(int status, CancellationToken ct = default)
        {
            LastStationStatus = status;
            return Task.CompletedTask;
        }

        // ===== 模拟 PLC 行为（测试/调试用） =====

        /// <summary>模拟触发信号上升沿</summary>
        public void SimulateTrigger(int triggerValue = 1)
        {
            Interlocked.Increment(ref TriggerCount);
            TriggerReceived?.Invoke(this, new PlcTriggerEventArgs(triggerValue));
        }

        /// <summary>模拟配方变更信号上升沿</summary>
        public void SimulateRecipeChange(int recipeId)
        {
            RecipeChanged?.Invoke(this, new RecipeChangeEventArgs(recipeId));
        }

        /// <summary>模拟 PLC 断开</summary>
        public void SimulateDisconnect()
        {
            if (!_connected) return;
            _connected = false;
            _logService?.LogWarn("PLC", "Mock PLC 模拟断开");
            PlcDisconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>模拟 PLC 重连成功</summary>
        public void SimulateReconnect()
        {
            if (_connected) return;
            _connected = true;
            _logService?.LogInfo("PLC", "Mock PLC 模拟重连成功");
            PlcReconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}
