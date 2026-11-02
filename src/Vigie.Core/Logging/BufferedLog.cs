using Vigie.Core.Platform;

namespace Vigie.Core.Logging;

/// <summary>
/// Keeps the most recent entries in memory for the journal view and raises an
/// event as each one is written. Thread-safe: entries arrive from the UI
/// thread, timers and process output.
/// </summary>
public sealed class BufferedLog(IClock clock, int capacity = 500) : ILog
{
    private readonly Lock _gate = new();
    private readonly Queue<LogEntry> _entries = new(capacity);

    public event EventHandler<LogEntry>? EntryWritten;

    public void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(clock.Now, level, message);
        lock (_gate)
        {
            if (_entries.Count == capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
        }

        EntryWritten?.Invoke(this, entry);
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries];
        }
    }
}
