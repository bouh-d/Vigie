namespace Vigie.Core.ClaudeCode;

/// <summary>A line of <c>--output-format stream-json</c> output that Vigie acts on.</summary>
public abstract record ClaudeStreamEvent;

/// <summary>Any other well-formed event. It shows the run is making progress.</summary>
public sealed record ActivityEvent : ClaudeStreamEvent
{
    public static ActivityEvent Instance { get; } = new();
}

public sealed record SessionInitEvent(string? SessionId) : ClaudeStreamEvent;

/// <param name="ErrorCategory">Claude Code's own category, for example
/// <c>rate_limit</c> or <c>overloaded</c>.</param>
public sealed record ApiRetryEvent(int Attempt, int MaxRetries, string? ErrorCategory, int? ErrorStatus) : ClaudeStreamEvent;

public sealed record PermissionDeniedEvent(string? ToolName) : ClaudeStreamEvent;

/// <summary>
/// The state of the account's usage limit: a <c>rate_limit_event</c>, or an
/// assistant message saying a request was refused because the limit was
/// reached.
/// </summary>
/// <param name="Status"><c>allowed</c>, <c>allowed_warning</c> or <c>rejected</c>.</param>
/// <param name="LimitType">For example <c>five_hour</c> or <c>seven_day</c>.</param>
/// <param name="UsingOverage">True when paid extra usage covers what is over
/// the limit, so requests still go through.</param>
public sealed record UsageLimitEvent(string? Status, DateTimeOffset? ResetsAt, string? LimitType, bool UsingOverage) : ClaudeStreamEvent
{
    /// <summary>True when requests are refused until the limit resets.</summary>
    public bool Reached => Status == "rejected" && !UsingOverage;
}

/// <param name="Subtype"><c>success</c>, or an <c>error_*</c> value such as
/// <c>error_max_turns</c>.</param>
/// <param name="ApiError">The kind of API error that ended the run, such as
/// <c>usage_limit_reached</c>, when Claude Code names one.</param>
public sealed record ResultEvent(
    string Subtype,
    bool IsError,
    int? NumTurns,
    decimal? TotalCostUsd,
    int PermissionDenials,
    long? DurationMs,
    string? ResultText,
    IReadOnlyList<string> Errors,
    string? ApiError) : ClaudeStreamEvent
{
    public bool Succeeded => !IsError && Subtype == "success";
}
