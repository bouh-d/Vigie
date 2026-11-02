namespace Vigie.Core.Logging;

public interface ILog
{
    void Write(LogLevel level, string message);
}

public static class LogExtensions
{
    public static void Info(this ILog log, string message) => log.Write(LogLevel.Info, message);

    public static void Warning(this ILog log, string message) => log.Write(LogLevel.Warning, message);

    public static void Error(this ILog log, string message) => log.Write(LogLevel.Error, message);
}
