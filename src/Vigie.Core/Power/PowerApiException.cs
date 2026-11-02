namespace Vigie.Core.Power;

/// <summary>
/// Raised when a <c>powrprof.dll</c> call returns a non-zero status. The status
/// is kept so callers can distinguish an access denial from other failures.
/// </summary>
public sealed class PowerApiException : Exception
{
    public PowerApiException(string operation, uint status)
        : base($"{operation} failed with status {status}.")
    {
        Operation = operation;
        Status = status;
    }

    public string Operation { get; }

    public uint Status { get; }

    public bool IsAccessDenied => Status == 5;
}
