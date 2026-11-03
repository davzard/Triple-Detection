using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TripleDetection.Application.Services;

public interface IWatchdogProcessStarter
{
    bool Start(string executablePath, string? arguments = null);
}

public sealed class DefaultWatchdogProcessStarter : IWatchdogProcessStarter
{
    public bool Start(string executablePath, string? arguments = null)
    {
        try
        {
            var psi = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            };

            if (!string.IsNullOrWhiteSpace(arguments))
                psi.Arguments = arguments;

            using var process = Process.Start(psi);
            return process != null;
        }
        catch
        {
            return false;
        }
    }
}

public enum WatchdogState
{
    Healthy,
    Restarting,
    FaultThresholdReached,
    ManualResetRequired,
    Recovered
}

public sealed class WatchdogFaultEventArgs : EventArgs
{
    public int RestartCount { get; init; }
    public DateTime TriggeredAt { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class AppWatchdogService : IDisposable
{
    private readonly object _gate = new();
    private readonly string _appExecutablePath;
    private readonly string _processName;
    private readonly int _checkIntervalSeconds;
    private readonly int _restartLimitPerHour;
    private readonly int _restartWindowHours;
    private readonly IWatchdogProcessStarter _processStarter;
    private readonly LoggingService? _logService;
    private readonly string _logPath;
    private readonly string _faultFlagPath;
    private readonly Queue<DateTime> _restartHistory = new();
    private Timer? _timer;
    private bool _disposed;

    public event EventHandler<WatchdogFaultEventArgs>? FaultThresholdReached;
    public event EventHandler? FaultCleared;

    public WatchdogState State { get; private set; } = WatchdogState.Healthy;

    public AppWatchdogService(
        LoggingService? logService,
        string appExecutablePath,
        string? processName = null,
        int checkIntervalSeconds = 30,
        int restartLimitPerHour = 3,
        int restartWindowHours = 1,
        IWatchdogProcessStarter? processStarter = null,
        string? logDirectory = null)
    {
        _logService = logService;
        _appExecutablePath = appExecutablePath;
        _processName = string.IsNullOrWhiteSpace(processName)
            ? Path.GetFileNameWithoutExtension(appExecutablePath)
            : processName;
        _checkIntervalSeconds = checkIntervalSeconds > 0 ? checkIntervalSeconds : 30;
        _restartLimitPerHour = restartLimitPerHour > 0 ? restartLimitPerHour : 3;
        _restartWindowHours = restartWindowHours > 0 ? restartWindowHours : 1;
        _processStarter = processStarter ?? new DefaultWatchdogProcessStarter();

        _logPath = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : logDirectory;
        Directory.CreateDirectory(_logPath);

        _faultFlagPath = Path.Combine(_logPath, "watchdog_fault.flag");
        if (File.Exists(_faultFlagPath))
        {
            State = WatchdogState.ManualResetRequired;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_timer != null) return;
            _timer = new Timer(_ => CheckOnce(), null, TimeSpan.FromSeconds(_checkIntervalSeconds), TimeSpan.FromSeconds(_checkIntervalSeconds));
            WriteLog("watchdog started");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public bool CheckOnce()
    {
        lock (_gate)
        {
            if (_disposed) return false;

            if (IsFaultLocked())
            {
                WriteLog("fault lock active; no auto restart allowed");
                return false;
            }

            if (IsProcessRunning())
            {
                if (State != WatchdogState.Healthy)
                {
                    State = WatchdogState.Healthy;
                    WriteLog("process recovered; watchdog reset to healthy");
                }
                return true;
            }

            return TryRestartApp();
        }
    }

    public void ResetFault()
    {
        lock (_gate)
        {
            _restartHistory.Clear();
            State = WatchdogState.Recovered;
            _logService?.LogInfo("Watchdog", "manual reset requested; fault state cleared");
            WriteLog("manual reset requested; fault state cleared");
            if (File.Exists(_faultFlagPath)) File.Delete(_faultFlagPath);
            FaultCleared?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsProcessRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(_processName);
            foreach (var process in processes)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, _appExecutablePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // some process modules can become inaccessible; fall through to the next candidate
                }
                finally
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }
        catch
        {
            // process lookup failed; treat as down to keep safety conservative
        }

        return false;
    }

    public bool IsFaultLocked() => State == WatchdogState.FaultThresholdReached || State == WatchdogState.ManualResetRequired;

    private bool TryRestartApp()
    {
        if (IsFaultLocked()) return false;

        var now = DateTime.Now;
        PruneHistory(now);
        if (_restartHistory.Count >= _restartLimitPerHour)
        {
            EnterFaultLock(_restartHistory.Count);
            return false;
        }

        _restartHistory.Enqueue(now);
        var restartCount = _restartHistory.Count;
        State = WatchdogState.Restarting;
        WriteLog($"process missing; restart attempt {restartCount}");

        var started = _processStarter.Start(_appExecutablePath);
        if (!started)
        {
            WriteLog($"restart attempt {restartCount} failed; app executable not started: {_appExecutablePath}");
        }

        return started;
    }

    private void EnterFaultLock(int restartCount)
    {
        State = WatchdogState.FaultThresholdReached;
        WriteLog($"restart threshold reached: {restartCount} attempts in {_restartWindowHours} hour(s); auto restart disabled");
        try
        {
            File.WriteAllText(_faultFlagPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - fault lock reached after {restartCount} restarts");
        }
        catch (Exception ex)
        {
            _logService?.LogError("Watchdog", $"failed to persist fault lock flag: {ex.Message}");
            WriteLog($"failed to persist fault lock flag: {ex.Message}");
        }

        var args = new WatchdogFaultEventArgs
        {
            RestartCount = restartCount,
            TriggeredAt = DateTime.Now,
            Message = $"检测软件已在 {_restartWindowHours} 小时内重启 {restartCount} 次，超过安全上限，已进入故障锁定状态。人工确认修复并执行复位后方可恢复自动监控。"
        };

        FaultThresholdReached?.Invoke(this, args);
        State = WatchdogState.ManualResetRequired;
    }

    private void PruneHistory(DateTime now)
    {
        var cutOff = now.AddHours(-_restartWindowHours);
        while (_restartHistory.Count > 0 && _restartHistory.Peek() < cutOff)
            _restartHistory.Dequeue();
    }

    private void WriteLog(string message)
    {
        _logService?.LogInfo("Watchdog", message);
        try
        {
            var file = Path.Combine(_logPath, "watchdog.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
            File.AppendAllLines(file, new[] { line });
        }
        catch
        {
            // silent on log failure to avoid blocking production paths
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _restartHistory.Clear();
    }
}
