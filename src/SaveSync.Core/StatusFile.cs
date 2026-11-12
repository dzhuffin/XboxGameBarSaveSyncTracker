using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SaveSync.Core
{
    /// <summary>What the companion publishes and the widget consumes.</summary>
    public sealed class StatusSnapshot
    {
        public const int CurrentSchema = 1;

        public int Schema { get; set; } = CurrentSchema;

        /// <summary>Refreshed on every write, even when nothing changed, so readers can detect a dead companion.</summary>
        public DateTime GeneratedUtc { get; set; }

        public IReadOnlyList<GameSyncStatus> Games { get; set; } = Array.Empty<GameSyncStatus>();
    }

    public static class StatusFile
    {
        public const string FileName = "status.json";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            Formatting = Formatting.Indented,
        };

        public static string Serialize(StatusSnapshot snapshot) => JsonConvert.SerializeObject(snapshot, Settings);

        public static StatusSnapshot? Deserialize(string json) => JsonConvert.DeserializeObject<StatusSnapshot>(json, Settings);

        /// <summary>Writes via a temp file and rename so readers never see a partial file.</summary>
        public static void Write(string directory, StatusSnapshot snapshot)
        {
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, FileName);
            var temp = target + ".tmp";
            File.WriteAllText(temp, Serialize(snapshot));
            // File.Replace fails with ERROR_INVALID_PARAMETER on package-virtualized paths; MoveFileEx works.
            if (!MoveFileEx(temp, target, MOVEFILE_REPLACE_EXISTING))
                throw new IOException("Could not publish status file", new System.ComponentModel.Win32Exception());
        }

        private const int MOVEFILE_REPLACE_EXISTING = 1;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existing, string target, int flags);

        public static StatusSnapshot? TryRead(string directory)
        {
            try
            {
                var path = Path.Combine(directory, FileName);
                if (!File.Exists(path)) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                return Deserialize(reader.ReadToEnd());
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
