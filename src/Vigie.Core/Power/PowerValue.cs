namespace Vigie.Core.Power;

/// <summary>
/// The AC (plugged in) and DC (on battery) index of a single power setting.
/// </summary>
public readonly record struct PowerValue(uint Ac, uint Dc);
