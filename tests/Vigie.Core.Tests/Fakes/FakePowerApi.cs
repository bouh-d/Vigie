using Vigie.Core.Power;

namespace Vigie.Core.Tests.Fakes;

public sealed class FakePowerApi : IPowerApi
{
    private const uint ErrorAccessDenied = 5;
    private const uint ErrorNotFound = 2;

    private readonly Dictionary<(Guid Scheme, Guid Subgroup, Guid Setting), PowerValue> _values = [];

    public Guid ActiveScheme { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public bool ModernStandby { get; set; }

    public PowerStatus Status { get; set; } = new(PowerSource.Ac, 100, BatterySaverOn: false, BatteryCritical: false);

    public bool FailCreateRequest { get; set; }

    public bool RequestUsesFallback { get; set; }

    public TimeSpan TimeSinceLastInput { get; set; } = TimeSpan.FromHours(1);

    public bool SuspendResult { get; set; } = true;

    public HashSet<Guid> DeniedWriteSettings { get; } = [];

    public HashSet<Guid> MissingSchemes { get; } = [];

    public bool ThrowOnActivate { get; set; }

    public List<Guid> Activations { get; } = [];

    public int LockCount { get; private set; }

    public int SuspendCount { get; private set; }

    public FakePowerRequest? LastRequest { get; private set; }

    public void Seed(Guid scheme, Guid subgroup, Guid setting, uint ac, uint dc)
        => _values[(scheme, subgroup, setting)] = new PowerValue(ac, dc);

    public PowerValue Current(Guid scheme, Guid subgroup, Guid setting) => _values[(scheme, subgroup, setting)];

    public IPowerRequest CreateSystemRequest(string reason, bool keepDisplayOn)
    {
        if (FailCreateRequest)
        {
            throw new PowerApiException("PowerCreateRequest", 1);
        }

        LastRequest = new FakePowerRequest(RequestUsesFallback);
        return LastRequest;
    }

    public Guid GetActiveSchemeGuid() => ActiveScheme;

    public PowerValue ReadValue(Guid scheme, Guid subgroup, Guid setting)
    {
        if (MissingSchemes.Contains(scheme))
        {
            throw new PowerApiException("PowerReadACValueIndex", ErrorNotFound);
        }

        return _values.TryGetValue((scheme, subgroup, setting), out PowerValue value)
            ? value
            : throw new PowerApiException("PowerReadACValueIndex", ErrorNotFound);
    }

    public void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        if (DeniedWriteSettings.Contains(setting))
        {
            throw new PowerApiException("PowerWriteACValueIndex", ErrorAccessDenied);
        }

        PowerValue current = _values.GetValueOrDefault((scheme, subgroup, setting));
        _values[(scheme, subgroup, setting)] = current.WithAc(value);
    }

    public void WriteDcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        if (DeniedWriteSettings.Contains(setting))
        {
            throw new PowerApiException("PowerWriteDCValueIndex", ErrorAccessDenied);
        }

        PowerValue current = _values.GetValueOrDefault((scheme, subgroup, setting));
        _values[(scheme, subgroup, setting)] = current.WithDc(value);
    }

    public void ActivateScheme(Guid scheme)
    {
        if (ThrowOnActivate)
        {
            throw new PowerApiException("PowerSetActiveScheme", 1);
        }

        Activations.Add(scheme);
    }

    public bool IsModernStandbyAvailable() => ModernStandby;

    public PowerStatus GetPowerStatus() => Status;

    public void LockWorkstation() => LockCount++;

    public bool TrySuspend()
    {
        SuspendCount++;
        return SuspendResult;
    }

    public TimeSpan GetTimeSinceLastInput() => TimeSinceLastInput;
}

public sealed class FakePowerRequest(bool usesFallback) : IPowerRequest
{
    public bool UsesFallback { get; } = usesFallback;

    public int ReassertCount { get; private set; }

    public bool Disposed { get; private set; }

    public void Reassert() => ReassertCount++;

    public void Dispose() => Disposed = true;
}
