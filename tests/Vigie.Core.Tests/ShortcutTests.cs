using Vigie.Core.Platform;
using Vigie.Core.Session;
using Xunit;

namespace Vigie.Core.Tests;

public class ShortcutTests
{
    private const int VkF9 = 0x78;
    private const int VkSpace = 0x20;
    private const int VkOemPeriod = 0xBE;

    [Theory]
    [InlineData(ShortcutModifiers.Control, (int)'C')]
    [InlineData(ShortcutModifiers.Alt, 0x73)]
    [InlineData(ShortcutModifiers.Shift, (int)'V')]
    public void ASingleModifierIsRefusedSoNoApplicationLosesItsOwnShortcuts(ShortcutModifiers modifiers, int key)
        => Assert.False(new Shortcut(modifiers, key).IsValid());

    [Theory]
    [InlineData(VkSpace)]
    [InlineData(VkOemPeriod)]
    public void OnlyLettersDigitsAndFunctionKeysAreAccepted(int key)
        => Assert.False(new Shortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, key).IsValid());

    [Fact]
    public void TheLabelUsesTheFrenchKeyNames()
    {
        var shortcut = new Shortcut(ShortcutModifiers.Control | ShortcutModifiers.Shift, VkF9);

        Assert.True(shortcut.IsValid());
        Assert.Equal("Ctrl + Maj + F9", shortcut.ToString());
    }
}
