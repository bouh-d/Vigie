namespace Vigie.Core.Power;

/// <summary>
/// A held power request that keeps the system, and optionally the display,
/// awake. It must be created on a thread that lives for the whole
/// session: the <c>SetThreadExecutionState</c> fallback belongs to its calling
/// thread.
/// </summary>
public interface IPowerRequest : IDisposable
{
    /// <summary>True when the request could not be created and the thread
    /// execution-state fallback is in use instead.</summary>
    bool UsesFallback { get; }

    /// <summary>
    /// Recreates the request after a resume. Windows ends power requests on a
    /// user-initiated sleep, so a running session asserts them again on wake.
    /// </summary>
    void Reassert();
}
