using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SaveSync.Core
{
    public interface IPackageInfoProvider
    {
        /// <summary>Returns install info for a package family, or null if the family is unknown/unregistered.</summary>
        PackageDetails? GetPackage(string familyName);
    }

    /// <summary>Scans %LOCALAPPDATA%\Packages\*\SystemAppData\wgs for per-game sync data.</summary>
    public sealed class GameScanner
    {
        private readonly string _packagesRoot;
        private readonly IPackageInfoProvider _packages;
        private readonly TimeSpan _skewTolerance;

        public GameScanner(string packagesRoot, IPackageInfoProvider packages, TimeSpan? skewTolerance = null)
        {
            _packagesRoot = packagesRoot;
            _packages = packages;
            _skewTolerance = skewTolerance ?? TimeSpan.FromSeconds(60);
        }

        public static string WgsPath(string packagesRoot, string familyName) =>
            Path.Combine(packagesRoot, familyName, "SystemAppData", "wgs");

        public IReadOnlyList<GameSyncStatus> Scan()
        {
            var result = new List<GameSyncStatus>();
            if (!Directory.Exists(_packagesRoot)) return result;

            foreach (var dir in Directory.EnumerateDirectories(_packagesRoot))
            {
                var family = Path.GetFileName(dir);
                var wgs = WgsPath(_packagesRoot, family);
                if (!Directory.Exists(wgs)) continue;

                var indexFiles = Directory.EnumerateDirectories(wgs)
                    .Select(d => Path.Combine(d, "containers.index"))
                    .Where(File.Exists)
                    .ToList();
                if (indexFiles.Count == 0) continue;

                var details = _packages.GetPackage(family)
                    ?? new PackageDetails { FamilyName = family, InstallState = InstallState.NotInstalled };
                result.Add(ScanPackage(details, wgs, indexFiles));
            }

            return result;
        }

        private GameSyncStatus ScanPackage(PackageDetails details, string wgs, List<string> indexFiles)
        {
            try
            {
                // Several user folders may exist; the most recently written index is the active one.
                var newest = indexFiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
                var index = ContainerIndexParser.Parse(ReadShared(newest));
                return SyncStateEvaluator.Evaluate(details, index, HasStagingFiles(wgs), _skewTolerance);
            }
            catch (Exception ex) when (ex is UnsupportedIndexException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return new GameSyncStatus
                {
                    FamilyName = details.FamilyName,
                    DisplayName = details.DisplayName ?? details.FamilyName,
                    InstallState = details.InstallState,
                    State = SyncState.Unknown,
                    Error = ex.Message,
                };
            }
        }

        private static bool HasStagingFiles(string wgs)
        {
            var staging = Path.Combine(wgs, "t");
            if (!Directory.Exists(staging)) return false;
            return Directory.EnumerateFileSystemEntries(staging).Any();
        }

        // Gaming Services may be writing the file; open with full sharing.
        private static byte[] ReadShared(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
