namespace Vigie.Core.ClaudeCode;

/// <summary>A line of <c>--output-format stream-json</c> output that Vigie acts on.</summary>
public abstract record ClaudeStreamEvent;

/// <summary>Any other well-formed event. It shows the run is making progress.</summary>
public sealed record ActivityEvent : ClaudeStreamEvent
{
    public static ActivityEvent Instance { get; } = new();
}

public sealed record SessionInitEvent(string? SessionId, string? Model) : ClaudeStreamEvent;

/// <param name="ErrorCategory">Claude Code's own category, for example
/// <c>rate_limit</c> or <c>overloaded</c>.</param>
public sealed record ApiRetryEvent(int Attempt, int MaxRetries, string? ErrorCategory, int? ErrorStatus) : ClaudeStreamEvent;

public sealed record PermissionDeniedEvent(string? ToolName) : ClaudeStreamEvent;

/// <param name="Subtype"><c>success</c>, or an <c>error_*</c> value such as
/// <c>error_max_turns</c>.</param>
public sealed record ResultEvent(
    string Subtype,
    bool IsError,
    int? NumTurns,
    decimal? TotalCostUsd,
    int PermissionDenials,
    long? DurationMs,
    string? ResultText,
    IReadOnlyList<string> Errors) : ClaudeStreamEvent
{
    public bool Succeeded => !IsError && Subtype == "success";
}
