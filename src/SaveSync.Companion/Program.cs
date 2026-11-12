using System.Runtime.InteropServices;
using SaveSync.Companion;

// Usage: SaveSync.Companion [--out <dir>] [--once]
//   --out   directory for status.json (default: the package's LocalState when packaged, else %LOCALAPPDATA%\SaveSyncTracker)
//   --once  scan once, print a summary and exit (diagnostics)
var packagesRoot = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "Packages");
var outDir = ArgValue("--out") ?? DefaultOutputDirectory();

if (args.Contains("--once"))
{
    AttachConsole(-1); // WinExe has no console of its own; borrow the parent's
    var games = new SaveSync.Core.GameScanner(packagesRoot, new WindowsPackageInfoProvider()).Scan();
    foreach (var g in games.OrderBy(g => g.DisplayName))
    {
        Console.WriteLine($"{g.DisplayName} [{g.InstallState}] {g.State} last sync {g.LastSyncedUtc?.ToLocalTime():g}{(g.Error is null ? "" : " error: " + g.Error)}");
        foreach (var c in g.Containers)
            Console.WriteLine($"    {c.Name,-26} {c.State,-8} modified {c.ModifiedUtc.ToLocalTime():g}  synced {c.LastSyncedUtc?.ToLocalTime():g}");
    }
    return;
}

// A single instance is enough; the widget launches the companion whenever it can't see a heartbeat.
using var mutex = new Mutex(true, @"Local\SaveSyncTracker.Companion", out var isFirst);
if (!isFirst) return;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
using var monitor = new SyncMonitor(packagesRoot, outDir, () => new WindowsPackageInfoProvider());
monitor.Run(cts.Token);

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string DefaultOutputDirectory()
{
    try { return Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
    catch { return Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "SaveSyncTracker"); }
}

[DllImport("kernel32.dll")]
static extern bool AttachConsole(int processId);
