using System;
using System.IO;
using System.Threading.Tasks;
using Prism.Events;

namespace TripleDetection.Application.Services;

public class LoggingService
{
    private readonly string _logPath;
    private readonly object _lockObj = new object();
    private readonly IEventAggregator _eventAggregator;

    public event EventHandler<LogEntry> OnLogAdded;

    public LoggingService(string logPath, IEventAggregator eventAggregator)
    {
        _logPath = logPath;
        _eventAggregator = eventAggregator;
        CleanupOldLogs();
    }

    private void CleanupOldLogs()
    {
        try
        {
            if (!Directory.Exists(_logPath)) return;
            var threshold = TimeSpan.FromDays(30);
            var now = DateTime.Now;
            foreach (var file in Directory.GetFiles(_logPath, "*.log"))
            {
                try
                {
                    var fi = new FileInfo(file);
                    if ((now - fi.LastWriteTime) > threshold) File.Delete(file);
                }
                catch { /* skip locked files */ }
            }
        }
        catch { /* skip if inaccessible */ }
    }

    /// <summary>
    /// 写日志。level 取值 INFO / WARN / ERROR。
    /// 注意：日志器自身故障必须静默（catch 不抛出），否则会在异常处理路径上二次抛错。
    /// </summary>
    public void Log(string message, string level = "INFO")
    {
        var entry = new LogEntry { Timestamp = DateTime.Now, Message = message, Level = level };
        _eventAggregator?.GetEvent<LogAddedEvent>()?.Publish(entry);
        OnLogAdded?.Invoke(this, entry);
        Task.Run(() => SaveLog(entry));
    }

    public void LogInfo(string category, string message) => Log($"[{category}] {message}", "INFO");

    public void LogWarn(string category, string message) => Log($"[{category}] {message}", "WARN");

    public void LogError(string category, string message) => Log($"[{category}] {message}", "ERROR");

    private void SaveLog(LogEntry entry)
    {
        try
        {
            if (!Directory.Exists(_logPath)) Directory.CreateDirectory(_logPath);
            var filename = Path.Combine(_logPath, "app.log");
            var line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss:ffff}\t[{entry.Level ?? "INFO"}]\t{entry.Message}";
            lock (_lockObj) { File.AppendAllText(filename, line + Environment.NewLine); }
        }
        catch { /* swallow logging errors */ }
    }

    public void Clear()
    {
        if (OnLogAdded == null) return;
        foreach (var handler in OnLogAdded.GetInvocationList())
            OnLogAdded -= (EventHandler<LogEntry>)handler;
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; }
    public string Level { get; set; } = "INFO";
}

// Placeholder - the actual LogAddedEvent will be defined in Presentation layer
public class LogAddedEvent : PubSubEvent<LogEntry> { }