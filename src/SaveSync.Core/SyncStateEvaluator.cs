using System;
using System.Collections.Generic;
using System.Linq;

namespace SaveSync.Core
{
    public static class SyncStateEvaluator
    {
        /// <summary>
        /// The ETag is the server write time. If it is older than the local modification time
        /// (beyond clock skew), the latest local change has not been uploaded yet.
        /// </summary>
        public static SyncState EvaluateContainer(ContainerEntry entry, TimeSpan skewTolerance)
        {
            if (entry.ETagTimeUtc is null) return SyncState.Unknown;
            return entry.ETagTimeUtc.Value + skewTolerance >= entry.ModifiedUtc ? SyncState.Synced : SyncState.Pending;
        }

        /// <param name="stagingActive">True when wgs\t holds staging files for this package (a sync pass is running).</param>
        public static GameSyncStatus Evaluate(
            PackageDetails package, ContainerIndex index, bool stagingActive, TimeSpan skewTolerance)
        {
            var containers = index.Containers.Select(c => new ContainerStatus
            {
                Name = c.Name,
                State = EvaluateContainer(c, skewTolerance),
                LastSyncedUtc = c.ETagTimeUtc,
                ModifiedUtc = c.ModifiedUtc,
                Size = c.Size,
            }).ToList();

            return new GameSyncStatus
            {
                FamilyName = package.FamilyName,
                DisplayName = package.DisplayName ?? package.FamilyName,
                InstallState = package.InstallState,
                State = Aggregate(containers, stagingActive),
                LastSyncedUtc = containers.Where(c => c.LastSyncedUtc.HasValue).Select(c => c.LastSyncedUtc).DefaultIfEmpty().Max(),
                Containers = containers,
            };
        }

        private static SyncState Aggregate(IReadOnlyCollection<ContainerStatus> containers, bool stagingActive)
        {
            if (stagingActive) return SyncState.Syncing;
            if (containers.Count == 0) return SyncState.Unknown;
            if (containers.Any(c => c.State == SyncState.Pending)) return SyncState.Pending;
            if (containers.Any(c => c.State == SyncState.Unknown)) return SyncState.Unknown;
            return SyncState.Synced;
        }
    }
}
