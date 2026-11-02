using System.Text.Json;
using System.Text.Json.Serialization;
using Vigie.Core.Platform;

namespace Vigie.Core.Power;

public enum JournalStatus
{
    Absent,
    Loaded,
    Unreadable,
}

/// <param name="RawContent">The file content as found, set when it could not
/// be parsed so it can be shown to the user verbatim.</param>
public sealed record JournalReadResult(JournalStatus Status, RestoreJournal? Journal, string? RawContent);

public sealed class RestoreJournalStore(IFileSystem fileSystem, string filePath)
{
    public string FilePath { get; } = filePath;

    public JournalReadResult Read()
    {
        if (!fileSystem.FileExists(FilePath))
        {
            return new JournalReadResult(JournalStatus.Absent, null, null);
        }

        string raw = fileSystem.ReadAllText(FilePath);
        try
        {
            RestoreJournal? journal = JsonSerializer.Deserialize(raw, RestoreJournalJsonContext.Default.RestoreJournal);
            if (journal is null || journal.FormatVersion != RestoreJournal.CurrentFormatVersion)
            {
                return new JournalReadResult(JournalStatus.Unreadable, null, raw);
            }

            return new JournalReadResult(JournalStatus.Loaded, journal, null);
        }
        catch (JsonException)
        {
            // Never guess at a damaged journal: the caller shows it as-is.
            return new JournalReadResult(JournalStatus.Unreadable, null, raw);
        }
    }

    public void Save(RestoreJournal journal)
    {
        string json = JsonSerializer.Serialize(journal, RestoreJournalJsonContext.Default.RestoreJournal);
        fileSystem.WriteAllTextAtomic(FilePath, json);
    }

    public void Delete()
    {
        if (fileSystem.FileExists(FilePath))
        {
            fileSystem.Delete(FilePath);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(RestoreJournal))]
internal sealed partial class RestoreJournalJsonContext : JsonSerializerContext;
