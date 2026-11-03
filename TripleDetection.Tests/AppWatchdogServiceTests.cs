#nullable enable
using System;
using System.IO;
using System.Threading;
using TripleDetection.Application.Services;
using Xunit;

namespace TripleDetection.Tests;

public class AppWatchdogServiceTests
{
    private sealed class StubStarter : IWatchdogProcessStarter
    {
        public int StartCalls;
        public bool Result { get; set; } = true;

        public bool Start(string executablePath, string? arguments = null)
        {
            Interlocked.Increment(ref StartCalls);
            return Result;
        }
    }

    [Fact]
    public void CheckOnce_WhenProcessMissing_TriggersRestartUntilThreshold()
    {
        var dir = Path.Combine(Path.GetTempPath(), "td_watchdog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var starter = new StubStarter();
            using var service = new AppWatchdogService(null, "C:\\app\\detector.exe", "detector", 1, 3, 1, starter, dir);

            service.CheckOnce();
            Assert.Equal(1, starter.StartCalls);
            Assert.Equal(WatchdogState.Restarting, service.State);

            service.CheckOnce();
            service.CheckOnce();
            Assert.Equal(3, starter.StartCalls);
            Assert.Equal(WatchdogState.Restarting, service.State);

            service.CheckOnce();
            Assert.Equal(WatchdogState.ManualResetRequired, service.State);
            Assert.True(service.IsFaultLocked());
            Assert.Equal(3, starter.StartCalls);
            Assert.True(File.Exists(Path.Combine(dir, "watchdog_fault.flag")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ResetFault_ClearsFaultLock()
    {
        var dir = Path.Combine(Path.GetTempPath(), "td_watchdog_reset_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var starter = new StubStarter();
            using var service = new AppWatchdogService(null, "C:\\app\\detector.exe", "detector", 1, 3, 1, starter, dir);

            service.CheckOnce();
            service.CheckOnce();
            service.CheckOnce();
            service.CheckOnce();
            Assert.Equal(WatchdogState.ManualResetRequired, service.State);

            service.ResetFault();
            Assert.Equal(WatchdogState.Recovered, service.State);
            Assert.False(service.IsFaultLocked());
            Assert.False(File.Exists(Path.Combine(dir, "watchdog_fault.flag")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FaultLock_PersistsAcrossServiceInstances_UntilManualReset()
    {
        var dir = Path.Combine(Path.GetTempPath(), "td_watchdog_persist_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var starter = new StubStarter();
            using (var service = new AppWatchdogService(null, "C:\\app\\detector.exe", "detector", 1, 3, 1, starter, dir))
            {
                service.CheckOnce();
                service.CheckOnce();
                service.CheckOnce();
                service.CheckOnce();
                Assert.True(service.IsFaultLocked());
            }

            using var restartedService = new AppWatchdogService(null, "C:\\app\\detector.exe", "detector", 1, 3, 1, starter, dir);
            Assert.True(restartedService.IsFaultLocked());
            restartedService.CheckOnce();
            Assert.Equal(3, starter.StartCalls);

            restartedService.ResetFault();
            Assert.False(restartedService.IsFaultLocked());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
