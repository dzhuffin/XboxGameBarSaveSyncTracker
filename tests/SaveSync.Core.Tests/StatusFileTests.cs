using System;
using System.IO;
using SaveSync.Core;
using Xunit;

namespace SaveSync.Core.Tests
{
    public class StatusFileTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "status-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Roundtrips_snapshot_through_atomic_write()
        {
            var snap = new StatusSnapshot
            {
                GeneratedUtc = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc),
                Games = new[]
                {
                    new GameSyncStatus
                    {
                        FamilyName = "F_1", DisplayName = "Game", State = SyncState.Pending, InstallState = InstallState.NeedsRepair,
                        LastSyncedUtc = new DateTime(2026, 11, 9, 11, 0, 0, DateTimeKind.Utc),
                        Containers = new[] { new ContainerStatus { Name = "s", State = SyncState.Synced, Size = 5 } },
                    },
                },
            };

            StatusFile.Write(_dir, snap);
            StatusFile.Write(_dir, snap); // second write exercises the replace path
            var back = StatusFile.TryRead(_dir)!;

            var g = Assert.Single(back.Games);
            Assert.Equal(SyncState.Pending, g.State);
            Assert.Equal(InstallState.NeedsRepair, g.InstallState);
            Assert.Equal(snap.GeneratedUtc, back.GeneratedUtc);
            Assert.Equal(DateTimeKind.Utc, back.GeneratedUtc.Kind);
            Assert.Equal("s", Assert.Single(g.Containers).Name);
            Assert.Contains("\"Pending\"", File.ReadAllText(Path.Combine(_dir, StatusFile.FileName)));
        }

        [Fact]
        public void TryRead_returns_null_for_missing_or_corrupt_file()
        {
            Assert.Null(StatusFile.TryRead(_dir));
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, StatusFile.FileName), "{ not json");
            Assert.Null(StatusFile.TryRead(_dir));
        }

        public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    }
}
