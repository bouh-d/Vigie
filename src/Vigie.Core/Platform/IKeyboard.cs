namespace Vigie.Core.Platform;

/// <summary>The modifier keys of a shortcut, with the values <c>RegisterHotKey</c> takes.</summary>
[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
}

/// <summary>
/// Whether modifier keys are held down. It is the only keyboard state Vigie
/// reads: nothing records which keys are typed.
/// </summary>
public interface IKeyboard
{
    bool IsAnyDown(ShortcutModifiers modifiers);
}
