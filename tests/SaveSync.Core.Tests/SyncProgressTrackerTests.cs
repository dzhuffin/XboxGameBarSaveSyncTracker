using System;
using System.Linq;
using SaveSync.Core;
using Xunit;

namespace SaveSync.Core.Tests
{
    public class SyncProgressTrackerTests
    {
        private static readonly DateTime T = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

        private static GameSyncStatus Game(int pending, int synced = 0) => new GameSyncStatus
        {
            FamilyName = "g",
            State = pending > 0 ? SyncState.Pending : SyncState.Synced,
            Containers = Enumerable.Repeat(SyncState.Pending, pending)
                .Concat(Enumerable.Repeat(SyncState.Synced, synced))
                .Select(s => new ContainerStatus { State = s }).ToList(),
        };

        [Fact]
        public void First_sighting_without_progress_stays_pending()
        {
            var g = Game(5);
            new SyncProgressTracker().Apply(new[] { g }, T);
            Assert.Equal(SyncState.Pending, g.State);
            Assert.Null(g.ProgressPercent);
        }

        [Fact]
        public void Falling_pending_count_is_syncing_with_percentage()
        {
            var tracker = new SyncProgressTracker();
            tracker.Apply(new[] { Game(4) }, T);
            var g = Game(3);
            tracker.Apply(new[] { g }, T.AddSeconds(5));
            Assert.Equal(SyncState.Syncing, g.State);
            Assert.Equal(25, g.ProgressPercent);

            g = Game(1);
            tracker.Apply(new[] { g }, T.AddSeconds(10));
            Assert.Equal(75, g.ProgressPercent);
        }

        [Fact]
        public void Stalled_uploads_fall_back_to_pending()
        {
            var tracker = new SyncProgressTracker();
            tracker.Apply(new[] { Game(4) }, T);
            tracker.Apply(new[] { Game(3) }, T.AddSeconds(5));
            var g = Game(3);
            tracker.Apply(new[] { g }, T.AddSeconds(5 + 61));
            Assert.Equal(SyncState.Pending, g.State);
            Assert.Null(g.ProgressPercent);
        }

        [Fact]
        public void Finished_sync_resets_so_the_next_batch_starts_fresh()
        {
            var tracker = new SyncProgressTracker();
            tracker.Apply(new[] { Game(2) }, T);
            tracker.Apply(new[] { Game(1) }, T.AddSeconds(5));
            tracker.Apply(new[] { Game(0, 2) }, T.AddSeconds(10));
            var g = Game(2);
            tracker.Apply(new[] { g }, T.AddMinutes(10));
            Assert.Equal(SyncState.Pending, g.State);
        }

        [Fact]
        public void Saves_written_during_sync_extend_the_total()
        {
            var tracker = new SyncProgressTracker();
            tracker.Apply(new[] { Game(2) }, T);
            tracker.Apply(new[] { Game(1) }, T.AddSeconds(5));
            var g = Game(3);
            tracker.Apply(new[] { g }, T.AddSeconds(8));
            Assert.Equal(25, g.ProgressPercent); // 1 of 4 uploaded
        }
    }
}
