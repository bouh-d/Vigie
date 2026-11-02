using Vigie.Core.ClaudeCode;
using Vigie.Core.Logging;
using Vigie.Core.Platform;
using Vigie.Core.Power;
using Vigie.Core.Session;
using Vigie.Core.Storage;

namespace Vigie.App;

/// <summary>
/// Builds the object graph by hand. The project keeps no dependency-injection
/// container; there is one graph, created once at start-up and in the
/// command-line restore modes.
/// </summary>
public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        Clock = new SystemClock();
        FileSystem = new SystemFileSystem();
        Environment = new SystemEnvironment();
        Registry = new WindowsRegistry();
        Power = new Win32PowerApi();
        Processes = new Win32ProcessApi();

        Storage = new VigieStorage(Environment, FileSystem);
        Storage.EnsureCreated();

        UiLog = new BufferedLog(Clock);
        FileLog = new FileLog(FileSystem, Clock, Storage.LogFilePath);
        Log = new CompositeLog(UiLog, FileLog);

        JournalStore = new RestoreJournalStore(FileSystem, Storage.RestoreJournalPath);
        JournalRestorer restorer = new(Power, JournalStore);
        CrashRecovery = new CrashRecovery(Registry, Processes, Environment);
        Leftover = new LeftoverJournal(JournalStore, restorer, Processes, Environment, Registry);

        Guard = new PowerSettingsGuard(Power, JournalStore);
        Session = new SessionController(Power, Guard, CrashRecovery, Log, Clock, Environment);
        Locator = new ClaudeCodeLocator(FileSystem, Environment);
        Preflight = new ClaudePreflight(Processes, Environment);
        Monitor = new ClaudeProcessMonitor(Processes, FileSystem, Clock);
    }

    public IClock Clock { get; }

    public IFileSystem FileSystem { get; }

    public IEnvironment Environment { get; }

    public IRegistry Registry { get; }

    public IPowerApi Power { get; }

    public IProcessApi Processes { get; }

    public VigieStorage Storage { get; }

    public BufferedLog UiLog { get; }

    public FileLog FileLog { get; }

    public ILog Log { get; }

    public RestoreJournalStore JournalStore { get; }

    public CrashRecovery CrashRecovery { get; }

    public LeftoverJournal Leftover { get; }

    public PowerSettingsGuard Guard { get; }

    public SessionController Session { get; }

    public ClaudeCodeLocator Locator { get; }

    public ClaudePreflight Preflight { get; }

    public ClaudeProcessMonitor Monitor { get; }

    public void Dispose()
    {
        Monitor.Dispose();
    }
}
