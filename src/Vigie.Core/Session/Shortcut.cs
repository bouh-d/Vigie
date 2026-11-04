using System.Globalization;
using System.Numerics;
using Vigie.Core.Platform;

namespace Vigie.Core.Session;

/// <summary>
/// A key combination chosen by the user, such as Ctrl + Alt + V. While it is
/// registered, Windows gives it to Vigie alone, so it needs two of Ctrl, Alt
/// and Shift: a single modifier would take over combinations such as Ctrl + C
/// in every application. The key is a letter, a digit or a function key, whose
/// names do not depend on the keyboard layout.
/// </summary>
public readonly record struct Shortcut(ShortcutModifiers Modifiers, int VirtualKey)
{
    private const int VkF1 = 0x70;
    private const int VkF24 = 0x87;

    public static Shortcut Default { get; } = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 'V');

    public bool IsValid()
    {
        const ShortcutModifiers allowed = ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift;
        return (Modifiers & ~allowed) == 0
            && BitOperations.PopCount((uint)Modifiers) >= 2
            && KeyName(VirtualKey) is not null;
    }

    /// <summary>The label shown in the window and the journal, with the French
    /// key names, for example « Ctrl + Maj + F9 ».</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(ShortcutModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            parts.Add("Maj");
        }

        parts.Add(KeyName(VirtualKey) ?? "?");
        return string.Join(" + ", parts);
    }

    private static string? KeyName(int virtualKey) => virtualKey switch
    {
        (>= 'A' and <= 'Z') or (>= '0' and <= '9') => ((char)virtualKey).ToString(),
        >= VkF1 and <= VkF24 => "F" + (virtualKey - VkF1 + 1).ToString(CultureInfo.InvariantCulture),
        _ => null,
    };
}
