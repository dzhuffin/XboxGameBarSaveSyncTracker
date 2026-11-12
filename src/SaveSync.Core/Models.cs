using System;
using System.Collections.Generic;

namespace SaveSync.Core
{
    /// <summary>One save container (slot) listed in a wgs containers.index file.</summary>
    public sealed class ContainerEntry
    {
        public string Name { get; set; } = "";

        /// <summary>Raw ETag as stored, e.g. "0x8DF3C2CC7081E22" (quotes included).</summary>
        public string ETag { get; set; } = "";

        /// <summary>The ETag decodes to the server write time (.NET ticks, UTC). Null if it can't be parsed.</summary>
        public DateTime? ETagTimeUtc { get; set; }

        /// <summary>Revision number; matches the container.N file name in the container folder.</summary>
        public int Revision { get; set; }

        public Guid FolderId { get; set; }

        /// <summary>Local last-modified time of the container.</summary>
        public DateTime ModifiedUtc { get; set; }

        public long Size { get; set; }
    }

    public sealed class ContainerIndex
    {
        public int Version { get; set; }

        /// <summary>Full package id, e.g. "Family!App".</summary>
        public string PackageId { get; set; } = "";

        public DateTime UpdatedUtc { get; set; }
        public string SyncId { get; set; } = "";
        public IReadOnlyList<ContainerEntry> Containers { get; set; } = Array.Empty<ContainerEntry>();
    }

    public enum SyncState
    {
        Unknown,
        Synced,
        Pending,
        Syncing,
    }

    public enum InstallState
    {
        Unknown,
        Installed,
        NeedsRepair,
        NotInstalled,
    }

    public sealed class PackageDetails
    {
        public string FamilyName { get; set; } = "";
        public string? DisplayName { get; set; }
        public InstallState InstallState { get; set; }
    }

    public sealed class ContainerStatus
    {
        public string Name { get; set; } = "";
        public SyncState State { get; set; }
        public DateTime? LastSyncedUtc { get; set; }
        public DateTime ModifiedUtc { get; set; }
        public long Size { get; set; }
    }

    public sealed class GameSyncStatus
    {
        public string FamilyName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public InstallState InstallState { get; set; }
        public SyncState State { get; set; }
        public DateTime? LastSyncedUtc { get; set; }

        /// <summary>0-99 while a sync is in progress, based on saves already uploaded; null otherwise.</summary>
        public int? ProgressPercent { get; set; }

        /// <summary>Non-null when the index couldn't be read or has an unsupported version.</summary>
        public string? Error { get; set; }

        public IReadOnlyList<ContainerStatus> Containers { get; set; } = Array.Empty<ContainerStatus>();
    }
}
