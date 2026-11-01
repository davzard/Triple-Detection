using Prism.Events;
using TripleDetection.Application.Services;

namespace TripleDetection.Presentation.Events
{
    /// <summary>
    /// Published when a log entry is added. Payload is the LogEntry.
    /// </summary>
    public class LogAddedEvent : PubSubEvent<LogEntry>
    {
    }
}