using System;
using System.IO;
using System.Linq;
using SaveSync.Core;
using Xunit;

namespace SaveSync.Core.Tests
{
    public class SyncStateTests
    {
        private static readonly DateTime T = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

        private static ContainerEntry Entry(DateTime? etag, DateTime modified) =>
            new ContainerEntry { Name = "x", ETagTimeUtc = etag, ModifiedUtc = modified };

        [Fact]
        public void Etag_after_modification_is_synced() =>
            Assert.Equal(SyncState.Synced, SyncStateEvaluator.EvaluateContainer(Entry(T.AddSeconds(50), T), Skew));

        [Fact]
        public void Etag_older_than_modification_is_pending() =>
            Assert.Equal(SyncState.Pending, SyncStateEvaluator.EvaluateContainer(Entry(T.AddHours(-1), T), Skew));

        [Fact]
        public void Small_clock_skew_is_tolerated() =>
            Assert.Equal(SyncState.Synced, SyncStateEvaluator.EvaluateContainer(Entry(T.AddSeconds(-30), T), Skew));

        [Fact]
        public void Missing_etag_is_unknown() =>
            Assert.Equal(SyncState.Unknown, SyncStateEvaluator.EvaluateContainer(Entry(null, T), Skew));

        [Fact]
        public void Game_state_aggregates_and_staging_means_syncing()
        {
            var pkg = new PackageDetails { FamilyName = "F", InstallState = InstallState.Installed };
            var data = IndexBuilder.Build("F!App", new[]
            {
                ("a", T.AddSeconds(5), T, (byte)1),
                ("b", T.AddHours(-1), T, (byte)1),
            });
            var index = ContainerIndexParser.Parse(data);

            Assert.Equal(SyncState.Pending, SyncStateEvaluator.Evaluate(pkg, index, false, Skew).State);
            Assert.Equal(SyncState.Syncing, SyncStateEvaluator.Evaluate(pkg, index, true, Skew).State);
        }
    }

    public class GameScannerTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "wgs-" + Guid.NewGuid().ToString("N"));

        private sealed class FakePackages : IPackageInfoProvider
        {
            public PackageDetails? GetPackage(string familyName) =>
                familyName == "Installed_1" ? new PackageDetails { FamilyName = familyName, DisplayName = "Cool Game", InstallState = InstallState.Installed } : null;
        }

        private void WriteIndex(string family, byte[] data, bool staging = false)
        {
            var user = Path.Combine(_root, family, "SystemAppData", "wgs", "00090000_0000");
            Directory.CreateDirectory(user);
            File.WriteAllBytes(Path.Combine(user, "containers.index"), data);
            if (staging)
            {
                var t = Path.Combine(_root, family, "SystemAppData", "wgs", "t");
                Directory.CreateDirectory(t);
                File.WriteAllText(Path.Combine(t, "tmp"), "");
            }
        }

        [Fact]
        public void Scan_reports_installed_orphaned_and_corrupt_packages()
        {
            var now = DateTime.UtcNow;
            var good = IndexBuilder.Build("Installed_1!App", new[] { ("s", now.AddSeconds(5), now, (byte)1) });
            WriteIndex("Installed_1", good);
            WriteIndex("Removed_2", good);
            WriteIndex("Broken_3", new byte[] { 1, 2, 3 });
            Directory.CreateDirectory(Path.Combine(_root, "NoWgs_4", "LocalState"));

            var result = new GameScanner(_root, new FakePackages()).Scan().ToDictionary(g => g.FamilyName);

            Assert.Equal(3, result.Count);
            Assert.Equal("Cool Game", result["Installed_1"].DisplayName);
            Assert.Equal(SyncState.Synced, result["Installed_1"].State);
            Assert.Equal(InstallState.NotInstalled, result["Removed_2"].InstallState);
            Assert.Equal(SyncState.Unknown, result["Broken_3"].State);
            Assert.NotNull(result["Broken_3"].Error);
        }

        [Fact]
        public void Staging_files_mark_game_as_syncing()
        {
            var now = DateTime.UtcNow;
            WriteIndex("Installed_1", IndexBuilder.Build("Installed_1!App", new[] { ("s", now, now, (byte)1) }), staging: true);
            var g = Assert.Single(new GameScanner(_root, new FakePackages()).Scan());
            Assert.Equal(SyncState.Syncing, g.State);
        }

        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
