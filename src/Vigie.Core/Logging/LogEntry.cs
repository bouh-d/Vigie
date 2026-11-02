namespace Vigie.Core.Logging;

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);
