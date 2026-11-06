using System.Text.Json;

namespace Vigie.Core.ClaudeCode;

/// <summary>
/// Reads one line of Claude Code's <c>stream-json</c> output. The format grows
/// over time, so unknown event types and fields are ignored rather than
/// treated as errors, and anything that is not a JSON object yields null.
/// </summary>
public static class StreamJsonParser
{
    /// <summary>The kind of API error Claude Code gives a request refused by
    /// the account's usage limit.</summary>
    public const string UsageLimitReached = "usage_limit_reached";

    // DateTimeOffset.MaxValue, in seconds since 1970.
    private const double MaxUnixSeconds = 253_402_300_799;

    public static ClaudeStreamEvent? Parse(string line)
    {
        ReadOnlySpan<char> trimmed = line.AsSpan().Trim();
        if (trimmed.IsEmpty || trimmed[0] != '{')
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return GetString(root, "type") switch
            {
                "system" => ParseSystem(root),
                "result" => ParseResult(root),
                "rate_limit_event" when GetObject(root, "rate_limit_info") is { } info => ParseUsageLimit(info, reached: false),
                "assistant" when GetString(root, "api_error") == UsageLimitReached => ParseLimitRefusal(root),
                _ => ActivityEvent.Instance,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ClaudeStreamEvent ParseSystem(JsonElement root)
    {
        return GetString(root, "subtype") switch
        {
            "init" => new SessionInitEvent(GetString(root, "session_id")),
            "api_retry" => new ApiRetryEvent(
                GetInt32(root, "attempt") ?? 0,
                GetInt32(root, "max_retries") ?? 0,
                GetString(root, "error"),
                GetInt32(root, "error_status")),
            "permission_denied" => new PermissionDeniedEvent(GetString(root, "tool_name")),
            _ => ActivityEvent.Instance,
        };
    }

    private static ResultEvent ParseResult(JsonElement root)
    {
        string subtype = GetString(root, "subtype") ?? string.Empty;
        bool isError = root.TryGetProperty("is_error", out JsonElement isErrorElement)
            ? isErrorElement.ValueKind == JsonValueKind.True
            : subtype != "success";

        int denials = root.TryGetProperty("permission_denials", out JsonElement denialsElement)
            && denialsElement.ValueKind == JsonValueKind.Array
                ? denialsElement.GetArrayLength()
                : 0;

        var errors = new List<string>();
        if (root.TryGetProperty("errors", out JsonElement errorsElement) && errorsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement error in errorsElement.EnumerateArray())
            {
                if (error.ValueKind == JsonValueKind.String && error.GetString() is { Length: > 0 } message)
                {
                    errors.Add(message);
                }
            }
        }

        return new ResultEvent(
            subtype,
            isError,
            GetInt32(root, "num_turns"),
            GetDecimal(root, "total_cost_usd"),
            denials,
            GetInt64(root, "duration_ms"),
            GetString(root, "result"),
            errors,
            GetString(root, "api_error"));
    }

    // The refused request's own record of the limit, in the shape of a
    // rate_limit_event. The refusal itself says the limit was reached.
    private static UsageLimitEvent ParseLimitRefusal(JsonElement root)
        => GetObject(root, "api_error_params") is { } parameters && GetObject(parameters, "rate_limit_info") is { } info
            ? ParseUsageLimit(info, reached: true)
            : new UsageLimitEvent("rejected", ResetsAt: null, LimitType: null, UsingOverage: false);

    private static UsageLimitEvent ParseUsageLimit(JsonElement info, bool reached)
    {
        DateTimeOffset? resetsAt = info.TryGetProperty("resetsAt", out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double seconds)
            && seconds is > 0 and <= MaxUnixSeconds
                ? DateTimeOffset.FromUnixTimeSeconds((long)seconds)
                : null;

        return new UsageLimitEvent(
            reached ? "rejected" : GetString(info, "status"),
            resetsAt,
            GetString(info, "rateLimitType"),
            !reached && info.TryGetProperty("isUsingOverage", out JsonElement overage) && overage.ValueKind == JsonValueKind.True);
    }

    private static JsonElement? GetObject(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number)
                ? number
                : null;

    private static long? GetInt64(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long number)
                ? number
                : null;

    private static decimal? GetDecimal(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out decimal number)
                ? number
                : null;
}
