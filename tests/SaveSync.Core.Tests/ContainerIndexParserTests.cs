using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SaveSync.Core;
using Xunit;

namespace SaveSync.Core.Tests
{
    internal static class IndexBuilder
    {
        public static byte[] Build(string packageId, IEnumerable<(string name, DateTime etagUtc, DateTime modifiedUtc, byte rev)> entries, int version = 14)
        {
            var list = new List<(string name, DateTime etagUtc, DateTime modifiedUtc, byte rev)>(entries);
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms, Encoding.Unicode);
            void Str(string s) { w.Write(s.Length); w.Write(Encoding.Unicode.GetBytes(s)); }

            w.Write(version); w.Write(list.Count); w.Write(0);
            Str(packageId);
            w.Write(DateTime.UtcNow.ToFileTimeUtc());
            w.Write(3);
            Str(Guid.NewGuid().ToString());
            w.Write(0L);
            foreach (var e in list)
            {
                Str(e.name); Str(e.name);
                Str("\"0x" + e.etagUtc.Ticks.ToString("X") + "\"");
                w.Write(e.rev); w.Write(1);
                w.Write(Guid.NewGuid().ToByteArray());
                w.Write(e.modifiedUtc.ToFileTimeUtc());
                w.Write(0L);
                w.Write(1234L);
            }
            return ms.ToArray();
        }
    }

    public class ContainerIndexParserTests
    {
        private static readonly DateTime T = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Parses_entries_and_decodes_etag_time()
        {
            var data = IndexBuilder.Build("Fam_abc!App", new[] { ("Quicksave0", T.AddSeconds(50), T, (byte)11) });
            var index = ContainerIndexParser.Parse(data);

            Assert.Equal("Fam_abc!App", index.PackageId);
            var c = Assert.Single(index.Containers);
            Assert.Equal("Quicksave0", c.Name);
            Assert.Equal(T.AddSeconds(50), c.ETagTimeUtc);
            Assert.Equal(T, c.ModifiedUtc);
            Assert.Equal(11, c.Revision);
            Assert.Equal(1234, c.Size);
        }

        [Fact]
        public void Rejects_unknown_version()
        {
            var data = IndexBuilder.Build("Fam!App", Array.Empty<(string, DateTime, DateTime, byte)>(), version: 15);
            Assert.Throws<UnsupportedIndexException>(() => ContainerIndexParser.Parse(data));
        }

        [Fact]
        public void Rejects_truncated_file()
        {
            var data = IndexBuilder.Build("Fam!App", new[] { ("A", T, T, (byte)1) });
            Assert.Throws<UnsupportedIndexException>(() => ContainerIndexParser.Parse(data[..(data.Length - 10)]));
        }
    }
}
