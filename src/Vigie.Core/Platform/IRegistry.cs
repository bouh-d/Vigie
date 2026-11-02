namespace Vigie.Core.Platform;

public interface IRegistry
{
    bool TryReadMachineInt32(string subKeyPath, string valueName, out int value);

    bool MachineSubKeyExists(string subKeyPath);

    void WriteCurrentUserString(string subKeyPath, string valueName, string value);

    void DeleteCurrentUserValue(string subKeyPath, string valueName);
}
