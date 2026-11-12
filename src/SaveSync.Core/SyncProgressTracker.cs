using System;
using System.Collections.Generic;
using System.Linq;

namespace SaveSync.Core
{
    /// <summary>
    /// Gaming Services gives no explicit "sync started" signal, so progress is inferred across scans:
    /// a falling pending-save count means uploads are happening. While uploads keep landing the game is
    /// reported as Syncing with a completion percentage; if they stop it falls back to Pending.
    /// </summary>
    public sealed class SyncProgressTracker
    {
        private sealed class Entry
        {
            public int Total;
            public int LastPending;
            public DateTime LastProgressUtc = DateTime.MinValue;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _activeWindow;

        public SyncProgressTracker(TimeSpan? activeWindow = null)
        {
            _activeWindow = activeWindow ?? TimeSpan.FromSeconds(60);
        }

        public void Apply(IEnumerable<GameSyncStatus> games, DateTime nowUtc)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var game in games)
            {
                var pending = game.Containers.Count(c => c.State == SyncState.Pending);
                if (game.Error != null || pending == 0)
                {
                    _entries.Remove(game.FamilyName);
                    continue;
                }

                seen.Add(game.FamilyName);
                if (!_entries.TryGetValue(game.FamilyName, out var entry))
                {
                    entry = new Entry { Total = pending, LastPending = pending };
                    _entries[game.FamilyName] = entry;
                }
                else if (pending < entry.LastPending)
                {
                    entry.LastProgressUtc = nowUtc;
                }
                else if (pending > entry.LastPending)
                {
                    // More saves were written while syncing; they extend the batch.
                    entry.Total += pending - entry.LastPending;
                }
                entry.LastPending = pending;

                var uploading = game.State == SyncState.Syncing || nowUtc - entry.LastProgressUtc < _activeWindow;
                if (!uploading) continue;

                game.State = SyncState.Syncing;
                game.ProgressPercent = Math.Max(0, Math.Min(99, (entry.Total - pending) * 100 / entry.Total));
            }

            foreach (var gone in _entries.Keys.Where(k => !seen.Contains(k)).ToList()) _entries.Remove(gone);
        }
    }
}
