using SaveSync.Core;

namespace SaveSync.Companion;

/// <summary>
/// Scans for game sync state and publishes it to status.json. Rescans when a watched wgs folder
/// changes, and at least every <see cref="HeartbeatInterval"/> so readers can detect a dead companion.
/// </summary>
public sealed class SyncMonitor : IDisposable
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    private readonly string _packagesRoot;
    private readonly string _outDir;
    private readonly Func<IPackageInfoProvider> _providerFactory;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly AutoResetEvent _dirty = new(true);
    private readonly SyncProgressTracker _progress = new();

    public SyncMonitor(string packagesRoot, string outDir, Func<IPackageInfoProvider> providerFactory)
    {
        _packagesRoot = packagesRoot;
        _outDir = outDir;
        _providerFactory = providerFactory;
    }

    public void Run(CancellationToken ct)
    {
        var lastPackageRefresh = DateTime.MinValue;
        IPackageInfoProvider? provider = null;

        while (!ct.IsCancellationRequested)
        {
            // Package registrations change rarely (install/uninstall), so refresh them on a slower cadence.
            if (provider is null || DateTime.UtcNow - lastPackageRefresh > TimeSpan.FromMinutes(1))
            {
                provider = SafeCreate();
                lastPackageRefresh = DateTime.UtcNow;
            }

            try
            {
                var games = new GameScanner(_packagesRoot, provider).Scan()
                    .OrderBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
                SyncWatchers(games.Select(g => g.FamilyName));
                _progress.Apply(games, DateTime.UtcNow);
                StatusFile.Write(_outDir, new StatusSnapshot { GeneratedUtc = DateTime.UtcNow, Games = games });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Transient (file in use); the next pass will retry.
            }

            WaitHandle.WaitAny(new[] { _dirty, ct.WaitHandle }, HeartbeatInterval);
            if (!ct.IsCancellationRequested) Thread.Sleep(Debounce);
        }
    }

    private IPackageInfoProvider SafeCreate()
    {
        try { return _providerFactory(); }
        catch { return new NullPackageInfoProvider(); }
    }

    private void SyncWatchers(IEnumerable<string> families)
    {
        var wanted = new HashSet<string>(families, StringComparer.OrdinalIgnoreCase);

        foreach (var gone in _watchers.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _watchers[gone].Dispose();
            _watchers.Remove(gone);
        }

        foreach (var family in wanted.Where(f => !_watchers.ContainsKey(f)))
        {
            var path = GameScanner.WgsPath(_packagesRoot, family);
            if (!Directory.Exists(path)) continue;
            var w = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
            };
            FileSystemEventHandler h = (_, _) => _dirty.Set();
            w.Changed += h; w.Created += h; w.Deleted += h;
            w.Renamed += (_, _) => _dirty.Set();
            w.EnableRaisingEvents = true;
            _watchers[family] = w;
        }
    }

    public void Dispose()
    {
        foreach (var w in _watchers.Values) w.Dispose();
        _dirty.Dispose();
    }

    private sealed class NullPackageInfoProvider : IPackageInfoProvider
    {
        public PackageDetails? GetPackage(string familyName) => null;
    }
}
