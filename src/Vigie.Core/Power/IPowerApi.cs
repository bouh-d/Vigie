namespace Vigie.Core.Power;

/// <summary>
/// The power-management surface the session logic depends on. The production
/// implementation forwards to <c>powrprof.dll</c>, <c>kernel32.dll</c> and
/// <c>user32.dll</c>; tests substitute an in-memory fake.
/// </summary>
public interface IPowerApi
{
    IPowerRequest CreateSystemRequest(string reason, bool keepDisplayOn);

    Guid GetActiveSchemeGuid();

    PowerValue ReadValue(Guid scheme, Guid subgroup, Guid setting);

    void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value);

    void WriteDcValue(Guid scheme, Guid subgroup, Guid setting, uint value);

    void ActivateScheme(Guid scheme);

    bool IsModernStandbyAvailable();

    PowerStatus GetPowerStatus();

    bool TryLockWorkstation();

    bool TrySuspend();

    TimeSpan GetTimeSinceLastInput();
}
