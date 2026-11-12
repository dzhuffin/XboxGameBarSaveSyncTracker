using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SaveSync.Core
{
    public sealed class UnsupportedIndexException : Exception
    {
        public UnsupportedIndexException(string message) : base(message) { }
    }

    /// <summary>
    /// Parses the undocumented Gaming Services wgs\...\containers.index file.
    /// The format was reverse engineered from version 14 files; other versions are rejected.
    /// </summary>
    public static class ContainerIndexParser
    {
        public const int SupportedVersion = 14;
        private const int MaxStringChars = 4096;
        private const int MaxContainers = 10000;

        public static ContainerIndex Parse(byte[] data)
        {
            try
            {
                using var reader = new BinaryReader(new MemoryStream(data, writable: false), Encoding.Unicode);
                return ParseCore(reader);
            }
            catch (EndOfStreamException)
            {
                throw new UnsupportedIndexException("containers.index is truncated.");
            }
            catch (ArgumentException ex)
            {
                throw new UnsupportedIndexException("containers.index is malformed: " + ex.Message);
            }
        }

        private static ContainerIndex ParseCore(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version != SupportedVersion)
                throw new UnsupportedIndexException($"Unsupported containers.index version {version}.");

            int count = r.ReadInt32();
            if (count < 0 || count > MaxContainers)
                throw new UnsupportedIndexException($"Implausible container count {count}.");
            r.ReadInt32();

            var index = new ContainerIndex
            {
                Version = version,
                PackageId = ReadString(r),
                UpdatedUtc = ReadFileTime(r),
            };
            r.ReadInt32();
            index.SyncId = ReadString(r);
            r.ReadInt64();

            var list = new List<ContainerEntry>(count);
            for (int i = 0; i < count; i++)
            {
                var entry = new ContainerEntry { Name = ReadString(r) };
                ReadString(r); // display name, always equal to Name in observed files
                entry.ETag = ReadString(r);
                entry.ETagTimeUtc = ParseETag(entry.ETag);
                entry.Revision = r.ReadByte();
                r.ReadInt32();
                entry.FolderId = new Guid(r.ReadBytes(16));
                entry.ModifiedUtc = ReadFileTime(r);
                r.ReadInt64();
                entry.Size = r.ReadInt64();
                list.Add(entry);
            }

            index.Containers = list;
            return index;
        }

        /// <summary>Decodes an ETag like "0x8DF3C2CC7081E22" into the server write time.</summary>
        public static DateTime? ParseETag(string etag)
        {
            var s = etag.Trim().Trim('"');
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (!long.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out long ticks)) return null;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return null;
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        private static DateTime ReadFileTime(BinaryReader r)
        {
            long ft = r.ReadInt64();
            try { return DateTime.FromFileTimeUtc(ft); }
            catch (ArgumentOutOfRangeException) { return DateTime.MinValue; }
        }

        private static string ReadString(BinaryReader r)
        {
            int chars = r.ReadInt32();
            if (chars < 0 || chars > MaxStringChars)
                throw new UnsupportedIndexException($"Implausible string length {chars}.");
            var bytes = r.ReadBytes(chars * 2);
            if (bytes.Length != chars * 2) throw new EndOfStreamException();
            return Encoding.Unicode.GetString(bytes);
        }
    }
}
