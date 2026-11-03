using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TripleDetection.Application.Services;

namespace TripleDetection.Infrastructure.Device
{
    /// <summary>
    /// Modbus TCP PLC 网关：500ms 轮询保持寄存器，上升沿触发 + 抖动过滤，
    /// 断线指数退避重连（1s→30s）。写操作非阻塞、失败仅记录日志。
    /// </summary>
    public class ModbusTcpPlcGateway : IPlcGateway
    {
        private readonly LoggingService _logService;
        private readonly PlcRegisterMap _map;
        private readonly string _ip;
        private readonly int _port;
        private readonly byte _unitId;
        private readonly int _pollIntervalMs;
        private readonly int _minTriggerIntervalMs;

        private readonly SemaphoreSlim _socketLock = new SemaphoreSlim(1, 1);
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private Task _pollTask;
        private int _txCounter;
        private volatile bool _connected;
        private long _lastTriggerAtMs;
        private int _lastTriggerValue;
        private int _lastRecipeChangeValue;
        private int _glitchCount;

        public ModbusTcpPlcGateway(
            LoggingService logService,
            PlcRegisterMap map,
            string ip,
            int port,
            byte unitId,
            int pollIntervalMs = 500,
            int minTriggerIntervalMs = 500)
        {
            _logService = logService;
            _map = map ?? new PlcRegisterMap();
            _ip = ip;
            _port = port;
            _unitId = unitId;
            _pollIntervalMs = pollIntervalMs > 0 ? pollIntervalMs : 500;
            _minTriggerIntervalMs = minTriggerIntervalMs;
        }

        public bool IsConnected => _connected;

        public event EventHandler<PlcTriggerEventArgs> TriggerReceived;
        public event EventHandler<RecipeChangeEventArgs> RecipeChanged;
        public event EventHandler PlcDisconnected;
        public event EventHandler PlcReconnected;

        /// <summary>被抖动过滤掉的触发次数（可观测性）</summary>
        public int GlitchCount => _glitchCount;

        public Task ConnectAsync(CancellationToken ct = default)
        {
            if (_pollTask != null) return Task.CompletedTask;
            _cts = new CancellationTokenSource();
            _pollTask = Task.Run(() => RunPollLoopAsync(_cts.Token));
            _logService?.LogInfo("PLC", $"Modbus TCP 网关启动：{_ip}:{_port}，UnitId={_unitId}，轮询 {_pollIntervalMs}ms");
            return Task.CompletedTask;
        }

        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            if (_pollTask != null)
            {
                try { await _pollTask.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { /* 轮询循环未能在超时内退出，忽略 */ }
            }
            CloseSocket();
            _connected = false;
            _cts?.Dispose();
            _cts = null;
            _pollTask = null;
        }

        public Task WriteResultAsync(DetectionVerdict verdict, CancellationToken ct = default)
            => WriteSingleAsync(_map.ResultOffset, (ushort)verdict, "RESULT");

        public async Task WriteRejectCommandAsync(int delayMs, int durationMs, CancellationToken ct = default)
        {
            // 若寄存器映射配置了延迟/持续参数寄存器，先写入
            if (_map.RejectDelayOffset >= 0)
                await WriteSingleAsync(_map.RejectDelayOffset, (ushort)delayMs, "REJECT_DELAY");
            if (_map.RejectDurationOffset >= 0)
                await WriteSingleAsync(_map.RejectDurationOffset, (ushort)durationMs, "REJECT_DURATION");
            // REJECT_CMD 脉冲：1 → 100ms → 0
            await WriteSingleAsync(_map.RejectCmdOffset, 1, "REJECT_CMD");
            try { await Task.Delay(100, ct); } catch (OperationCanceledException) { }
            await WriteSingleAsync(_map.RejectCmdOffset, 0, "REJECT_CMD");
        }

        public async Task WriteLineStopAsync(int alarmCode, CancellationToken ct = default)
        {
            await WriteSingleAsync(_map.AlarmCodeOffset, (ushort)alarmCode, "ALARM_CODE");
            await WriteSingleAsync(_map.LineStopOffset, 1, "LINE_STOP");
        }

        public Task WriteAlarmCodeAsync(int alarmCode, CancellationToken ct = default)
            => WriteSingleAsync(_map.AlarmCodeOffset, (ushort)alarmCode, "ALARM_CODE");

        public Task WriteStationStatusAsync(int status, CancellationToken ct = default)
            => WriteSingleAsync(_map.StationStatusOffset, (ushort)status, "STATION_STATUS");

        public Task WriteHeartbeatAsync(int value, CancellationToken ct = default)
            => WriteSingleAsync(_map.HeartbeatOffset, (ushort)value, "HEARTBEAT");

        // ===== 轮询与重连 =====

        private async Task RunPollLoopAsync(CancellationToken ct)
        {
            int backoffMs = 1000;
            while (!ct.IsCancellationRequested)
            {
                if (!TryConnect())
                {
                    RaiseDisconnectedIfWasConnected();
                    try { await Task.Delay(backoffMs, ct); }
                    catch (OperationCanceledException) { break; }
                    backoffMs = Math.Min(backoffMs * 2, 30000);
                    continue;
                }

                if (!_connected)
                {
                    _connected = true;
                    _logService?.LogInfo("PLC", "Modbus TCP 已连接");
                    PlcReconnected?.Invoke(this, EventArgs.Empty);
                }
                backoffMs = 1000;

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var values = await ReadHoldingRegistersAsync(0, 16);
                        if (values == null) break; // 通信失败，退出内层循环触发重连
                        ProcessRegisters(values);
                        try { await Task.Delay(_pollIntervalMs, ct); }
                        catch (OperationCanceledException) { break; }
                    }
                }
                catch (Exception ex)
                {
                    _logService?.LogWarn("PLC", $"轮询循环异常：{ex.Message}");
                }

                CloseSocket();
                RaiseDisconnectedIfWasConnected();
            }
        }

        private void ProcessRegisters(ushort[] values)
        {
            // 触发信号上升沿 + 抖动过滤
            int trigger = values[_map.TriggerOffset];
            if (trigger != 0 && _lastTriggerValue == 0)
            {
                long now = Environment.TickCount64;
                if (now - _lastTriggerAtMs < _minTriggerIntervalMs)
                {
                    Interlocked.Increment(ref _glitchCount);
                    _lastTriggerValue = trigger;
                    return;
                }
                _lastTriggerAtMs = now;
                TriggerReceived?.Invoke(this, new PlcTriggerEventArgs(trigger));
            }
            _lastTriggerValue = trigger;

            // 配方变更上升沿
            int recipeChange = values[_map.RecipeChangeOffset];
            if (recipeChange != 0 && _lastRecipeChangeValue == 0)
            {
                RecipeChanged?.Invoke(this, new RecipeChangeEventArgs(values[_map.RecipeIdOffset]));
            }
            _lastRecipeChangeValue = recipeChange;
        }

        private void RaiseDisconnectedIfWasConnected()
        {
            if (!_connected) return;
            _connected = false;
            _logService?.LogWarn("PLC", "Modbus TCP 连接断开，进入重连退避");
            PlcDisconnected?.Invoke(this, EventArgs.Empty);
        }

        private bool TryConnect()
        {
            try
            {
                CloseSocket();
                _client = new TcpClient
                {
                    ReceiveTimeout = 1000,
                    SendTimeout = 1000
                };
                _client.Connect(_ip, _port);
                _stream = _client.GetStream();
                return true;
            }
            catch
            {
                CloseSocket();
                return false;
            }
        }

        private void CloseSocket()
        {
            try { _stream?.Dispose(); } catch { /* 忽略 */ }
            try { _client?.Close(); } catch { /* 忽略 */ }
            _stream = null;
            _client = null;
        }

        // ===== Modbus 读写（锁保护，事务号递增） =====

        private byte NextTransactionId() => unchecked((byte)Interlocked.Increment(ref _txCounter));

        private async Task<ushort[]> ReadHoldingRegistersAsync(int start, int quantity)
        {
            byte txId = NextTransactionId();
            byte[] request = ModbusFrame.BuildReadHoldingRequest(txId, _unitId, start, quantity);
            await _socketLock.WaitAsync();
            try
            {
                if (_stream == null) return null;
                await _stream.WriteAsync(request, 0, request.Length);
                byte[] frame = await ReadResponseFrameAsync(txId);
                return ModbusFrame.ParseReadResponse(frame, txId);
            }
            catch (Exception ex)
            {
                _logService?.LogWarn("PLC", $"读寄存器失败：{ex.Message}");
                return null;
            }
            finally
            {
                _socketLock.Release();
            }
        }

        private async Task WriteSingleAsync(int register, ushort value, string name)
        {
            if (!_connected) return;
            byte txId = NextTransactionId();
            byte[] request = ModbusFrame.BuildWriteSingleRequest(txId, _unitId, register, value);
            await _socketLock.WaitAsync();
            try
            {
                if (_stream == null) return;
                await _stream.WriteAsync(request, 0, request.Length);
                byte[] frame = await ReadResponseFrameAsync(txId);
                ModbusFrame.ParseWriteResponse(frame, txId, ModbusFrame.FnWriteSingle);
            }
            catch (Exception ex)
            {
                _logService?.LogWarn("PLC", $"写 {name} 失败：{ex.Message}");
            }
            finally
            {
                _socketLock.Release();
            }
        }

        /// <summary>读取完整响应帧：先读 7 字节 MBAP 头，再按长度字段读剩余部分</summary>
        private async Task<byte[]> ReadResponseFrameAsync(byte txId)
        {
            byte[] header = new byte[7];
            await ReadExactAsync(header, 7);
            int length = (header[4] << 8) | header[5]; // 标准 MBAP：长度 = 单元 ID(1) + PDU
            int remaining = length - 1;
            if (remaining < 0) throw new ModbusException($"MBAP 长度字段非法：{length}");
            byte[] rest = new byte[remaining];
            await ReadExactAsync(rest, remaining);
            var frame = new byte[7 + remaining];
            Array.Copy(header, 0, frame, 0, 7);
            Array.Copy(rest, 0, frame, 7, remaining);
            return frame;
        }

        private async Task ReadExactAsync(byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = await _stream.ReadAsync(buffer, offset, count - offset);
                if (read <= 0) throw new ModbusException("连接已关闭");
                offset += read;
            }
        }
    }
}
