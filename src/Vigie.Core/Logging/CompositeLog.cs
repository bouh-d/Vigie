namespace Vigie.Core.Logging;

public sealed class CompositeLog(params ILog[] targets) : ILog
{
    public void Write(LogLevel level, string message)
    {
        foreach (ILog target in targets)
        {
            target.Write(level, message);
        }
    }
}
