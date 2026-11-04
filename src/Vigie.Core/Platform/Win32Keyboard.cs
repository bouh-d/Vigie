using Vigie.Core.Interop;

namespace Vigie.Core.Platform;

public sealed class Win32Keyboard : IKeyboard
{
    public bool IsAnyDown(ShortcutModifiers modifiers)
        => (modifiers.HasFlag(ShortcutModifiers.Control) && IsDown(NativeConstants.VkControl))
            || (modifiers.HasFlag(ShortcutModifiers.Alt) && IsDown(NativeConstants.VkMenu))
            || (modifiers.HasFlag(ShortcutModifiers.Shift) && IsDown(NativeConstants.VkShift));

    // The most significant bit is set while the key is down.
    private static bool IsDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
