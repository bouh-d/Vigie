using Microsoft.Win32;

namespace Vigie.Core.Platform;

public sealed class WindowsRegistry : IRegistry
{
    public bool TryReadMachineInt32(string subKeyPath, string valueName, out int value)
    {
        value = 0;
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(subKeyPath);
        if (key?.GetValue(valueName) is int dword)
        {
            value = dword;
            return true;
        }

        return false;
    }

    public bool MachineSubKeyExists(string subKeyPath)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(subKeyPath);
        return key is not null;
    }

    public void WriteCurrentUserString(string subKeyPath, string valueName, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteCurrentUserValue(string subKeyPath, string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
