using System;
using System.IO;
using System.Threading;
using TripleDetection.Application.Services;
using TripleDetection.Infrastructure.Device;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>Mock PLC 网关：写入记录 + 模拟事件。</summary>
    public class MockPlcGatewayTests
    {
        private readonly MockPlcGateway _gw;

        public MockPlcGatewayTests()
        {
            var dir = Path.Combine(Path.GetTempPath(), "td_mock_plc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _gw = new MockPlcGateway(new LoggingService(dir, null));
        }

        [Fact]
        public async System.Threading.Tasks.Task Connect_SetsConnected()
        {
            Assert.False(_gw.IsConnected);
            await _gw.ConnectAsync();
            Assert.True(_gw.IsConnected);
            await _gw.DisconnectAsync();
            Assert.False(_gw.IsConnected);
        }

        [Fact]
        public async System.Threading.Tasks.Task WriteResult_RecordsVerdict()
        {
            await _gw.WriteResultAsync(DetectionVerdict.Ng);
            Assert.Equal(DetectionVerdict.Ng, _gw.LastResult);
        }

        [Fact]
        public async System.Threading.Tasks.Task WriteReject_RecordsTiming()
        {
            await _gw.WriteRejectCommandAsync(80, 300);
            Assert.Equal(80, _gw.LastRejectDelayMs);
            Assert.Equal(300, _gw.LastRejectDurationMs);
        }

        [Fact]
        public async System.Threading.Tasks.Task WriteLineStop_RecordsAlarmCode()
        {
            await _gw.WriteLineStopAsync(1001);
            Assert.Equal(1001, _gw.LastLineStopAlarmCode);
        }

        [Fact]
        public async System.Threading.Tasks.Task WriteStationStatus_RecordsStatus()
        {
            await _gw.WriteStationStatusAsync(2);
            Assert.Equal(2, _gw.LastStationStatus);
        }

        [Fact]
        public void SimulateTrigger_RaisesEvent_AndCounts()
        {
            PlcTriggerEventArgs args = null;
            _gw.TriggerReceived += (s, e) => args = e;

            _gw.SimulateTrigger(1);
            _gw.SimulateTrigger();

            Assert.Equal(2, _gw.TriggerCount);
            Assert.NotNull(args);
            Assert.Equal(1, args.TriggerValue);
        }

        [Fact]
        public void SimulateRecipeChange_RaisesEvent()
        {
            RecipeChangeEventArgs args = null;
            _gw.RecipeChanged += (s, e) => args = e;

            _gw.SimulateRecipeChange(42);

            Assert.NotNull(args);
            Assert.Equal(42, args.RecipeId);
        }

        [Fact]
        public async System.Threading.Tasks.Task SimulateDisconnect_AndReconnect_RaiseEvents()
        {
            await _gw.ConnectAsync();
            var disconnected = new ManualResetEventSlim(false);
            var reconnected = new ManualResetEventSlim(false);
            _gw.PlcDisconnected += (s, e) => disconnected.Set();
            _gw.PlcReconnected += (s, e) => reconnected.Set();

            _gw.SimulateDisconnect();
            Assert.True(disconnected.Wait(1000));
            Assert.False(_gw.IsConnected);

            _gw.SimulateReconnect();
            Assert.True(reconnected.Wait(1000));
            Assert.True(_gw.IsConnected);
        }
    }
}
