using System;
using System.Collections.Generic;
using System.Linq;
using SaveSync.Core;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace SaveSync.Widget
{
    public sealed class GameItem
    {
        public string Title { get; set; }
        public string StateText { get; set; }
        public string Detail { get; set; }
        public Brush StateBrush { get; set; }
        public double Opacity { get; set; } = 1;
    }

    public enum SortMode { Name, LastSynced, Status }

    public static class GameItemFactory
    {
        public static List<GameItem> Build(StatusSnapshot snapshot, bool showUninstalled, string search, SortMode sort, DateTime nowUtc)
        {
            var games = snapshot.Games
                .Where(g => showUninstalled || g.InstallState == InstallState.Installed || g.InstallState == InstallState.Unknown)
                .Where(g => MatchesSearch(g.DisplayName, search));

            IOrderedEnumerable<GameSyncStatus> ordered;
            switch (sort)
            {
                case SortMode.LastSynced:
                    // Most recently synced first; games that never synced go last.
                    ordered = games.OrderByDescending(g => g.LastSyncedUtc ?? DateTime.MinValue);
                    break;
                case SortMode.Status:
                    ordered = games.OrderBy(g => StateRank(g.State));
                    break;
                default:
                    ordered = games.OrderBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase);
                    break;
            }

            return ordered
                .ThenBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => ToItem(g, nowUtc))
                .ToList();
        }

        // Every word typed must appear somewhere in the name, so "blood dawn" finds "The Blood of Dawnwalker".
        private static bool MatchesSearch(string name, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            name = name ?? string.Empty;
            return search.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .All(word => name.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        // Anything not fully synced sorts above synced games.
        private static int StateRank(SyncState s)
        {
            switch (s)
            {
                case SyncState.Pending: return 0;
                case SyncState.Syncing: return 1;
                case SyncState.Synced: return 3;
                default: return 2;
            }
        }

        private static GameItem ToItem(GameSyncStatus g, DateTime nowUtc)
        {
            var notInstalled = g.InstallState == InstallState.NotInstalled;
            var parts = new List<string>();

            if (g.Error != null) parts.Add("Can't read sync data");
            else if (g.LastSyncedUtc.HasValue) parts.Add("Last synced " + RelativeTime(g.LastSyncedUtc.Value, nowUtc));

            var pending = g.Containers.Count(c => c.State == SyncState.Pending);
            if (g.Containers.Count > 0)
                parts.Add(pending > 0 ? $"{pending} of {g.Containers.Count} saves waiting to upload" : $"{g.Containers.Count} saves");

            if (notInstalled) parts.Add("not installed (cached data)");
            else if (g.InstallState == InstallState.NeedsRepair) parts.Add("needs repair");

            return new GameItem
            {
                Title = g.DisplayName,
                StateText = StateLabel(g.State) + (g.State == SyncState.Syncing && g.ProgressPercent.HasValue ? $" {g.ProgressPercent}%" : ""),
                StateBrush = new SolidColorBrush(StateColor(g.State)),
                Detail = string.Join(" · ", parts),
                Opacity = notInstalled || g.InstallState == InstallState.NeedsRepair ? 0.55 : 1,
            };
        }

        private static string StateLabel(SyncState s)
        {
            switch (s)
            {
                case SyncState.Synced: return "Synced";
                case SyncState.Syncing: return "Syncing…";
                case SyncState.Pending: return "Pending upload";
                default: return "Unknown";
            }
        }

        private static Color StateColor(SyncState s)
        {
            switch (s)
            {
                case SyncState.Synced: return Color.FromArgb(255, 16, 185, 129);
                case SyncState.Syncing: return Color.FromArgb(255, 59, 130, 246);
                case SyncState.Pending: return Color.FromArgb(255, 245, 158, 11);
                default: return Color.FromArgb(255, 148, 163, 184);
            }
        }

        public static string RelativeTime(DateTime thenUtc, DateTime nowUtc)
        {
            var d = nowUtc - thenUtc;
            if (d < TimeSpan.FromSeconds(45)) return "just now";
            if (d < TimeSpan.FromMinutes(60)) return $"{(int)Math.Round(d.TotalMinutes)} min ago";
            if (d < TimeSpan.FromHours(24)) return $"{(int)Math.Round(d.TotalHours)} h ago";
            if (d < TimeSpan.FromDays(30)) return $"{(int)Math.Round(d.TotalDays)} d ago";
            return thenUtc.ToLocalTime().ToString("d");
        }
    }
}
