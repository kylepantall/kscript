using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace KScript.Compile
{
    /// <summary>What a file in a .NET single-file bundle is, as the host reads it.</summary>
    internal enum BundleFileType : byte
    {
        Unknown = 0,
        Assembly = 1,
        NativeBinary = 2,
        DepsJson = 3,
        RuntimeConfigJson = 4,
        Symbols = 5,
    }

    /// <summary>A file inside a single-file bundle. <see cref="Data"/> holds the stored bytes (compressed when
    /// <see cref="CompressedSize"/> isn't 0).</summary>
    internal sealed class BundleEntry
    {
        public string RelativePath { get; set; }
        public BundleFileType Type { get; set; }
        public long Size { get; set; }
        public long CompressedSize { get; set; }
        public byte[] Data { get; set; }

        /// <summary>The file's contents, decompressed.</summary>
        public byte[] Content()
        {
            if (CompressedSize == 0) return Data;
            using var input = new System.IO.Compression.DeflateStream(new MemoryStream(Data), System.IO.Compression.CompressionMode.Decompress);
            var output = new MemoryStream((int)Size);
            input.CopyTo(output);
            return output.ToArray();
        }

        public static BundleEntry FromBytes(string path, BundleFileType type, byte[] data) =>
            new BundleEntry { RelativePath = path, Type = type, Size = data.Length, CompressedSize = 0, Data = data };
    }

    /// <summary>
    /// Reads and writes .NET single-file bundles (format 6, .NET 6 and later): the host executable followed by the
    /// files and a manifest. A release kscript is one; <c>kscript compile</c> takes it apart, keeps the files a script
    /// needs, adds the script, and writes a new one. The host finds the manifest through an offset stored next to a
    /// fixed 32-byte signature inside it.
    /// </summary>
    internal sealed class SingleFileBundle
    {
        /// <summary>The marker the .NET host carries; the 8 bytes before it hold the manifest's offset.</summary>
        internal static readonly byte[] Signature =
        {
            0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
            0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae,
        };

        public byte[] Host { get; set; }
        public uint MajorVersion { get; set; } = 6;
        public uint MinorVersion { get; set; }
        public string BundleId { get; set; }
        public ulong Flags { get; set; }
        public List<BundleEntry> Entries { get; } = new List<BundleEntry>();

        public BundleEntry Find(string relativePath) =>
            Entries.FirstOrDefault(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        public BundleEntry FindType(BundleFileType type) => Entries.FirstOrDefault(e => e.Type == type);

        /// <summary>Where the 8-byte manifest offset sits in a host, or -1 when it has no bundle marker.</summary>
        internal static int MarkerOffset(byte[] host)
        {
            int at = IndexOf(host, Signature, 0);
            return at < 8 ? -1 : at - 8;
        }

        internal static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            for (int i = Array.IndexOf(haystack, needle[0], start); i >= 0 && i <= haystack.Length - needle.Length; i = Array.IndexOf(haystack, needle[0], i + 1))
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
            }
            return -1;
        }

        public static SingleFileBundle Read(string path) => Read(File.ReadAllBytes(path), Path.GetFileName(path));

        public static SingleFileBundle Read(byte[] file, string name)
        {
            int marker = MarkerOffset(file);
            long headerOffset = marker < 0 ? 0 : BitConverter.ToInt64(file, marker);
            if (headerOffset <= 0 || headerOffset >= file.Length)
                throw new CompileException($"{name} isn't a single-file kscript, so it can't be used to build programs. Use a kscript from a release.");

            var reader = new BinaryReader(new MemoryStream(file), Encoding.UTF8);
            reader.BaseStream.Position = headerOffset;
            var bundle = new SingleFileBundle { MajorVersion = reader.ReadUInt32(), MinorVersion = reader.ReadUInt32() };
            if (bundle.MajorVersion != 6)
                throw new CompileException($"{name} was built with a .NET version kscript compile doesn't know (bundle format {bundle.MajorVersion}).");
            int count = reader.ReadInt32();
            bundle.BundleId = reader.ReadString();
            reader.ReadInt64(); reader.ReadInt64(); // deps.json offset and size: also listed among the files
            reader.ReadInt64(); reader.ReadInt64(); // runtimeconfig.json offset and size
            bundle.Flags = reader.ReadUInt64();

            long firstFile = headerOffset;
            for (int i = 0; i < count; i++)
            {
                long offset = reader.ReadInt64();
                long size = reader.ReadInt64();
                long compressed = reader.ReadInt64();
                var type = (BundleFileType)reader.ReadByte();
                string relative = reader.ReadString();
                long stored = compressed == 0 ? size : compressed;
                bundle.Entries.Add(new BundleEntry
                {
                    RelativePath = relative,
                    Type = type,
                    Size = size,
                    CompressedSize = compressed,
                    Data = file.AsSpan((int)offset, (int)stored).ToArray(),
                });
                firstFile = Math.Min(firstFile, offset);
            }

            bundle.Host = file.AsSpan(0, (int)firstFile).ToArray();
            BitConverter.GetBytes(0L).CopyTo(bundle.Host, marker); // a fresh host: no manifest yet
            return bundle;
        }

        /// <summary>
        /// Writes the bundle: host, files, manifest. Assemblies start on 4 KB boundaries so every platform can map them.
        /// Returns the file's length; <paramref name="hostLength"/> is where the files start.
        /// </summary>
        public byte[] ToBytes()
        {
            byte[] host = (byte[])Host.Clone();
            int marker = MarkerOffset(host);
            if (marker < 0) throw new CompileException("The program's launcher has no bundle marker.");

            var output = new MemoryStream();
            output.Write(host);
            var placed = new List<(BundleEntry Entry, long Offset)>();
            foreach (BundleEntry entry in Entries)
            {
                if (entry.Type == BundleFileType.Assembly) Pad(output, 4096);
                placed.Add((entry, output.Position));
                output.Write(entry.Data);
            }

            Pad(output, 16);
            long header = output.Position;
            var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
            writer.Write(MajorVersion);
            writer.Write(MinorVersion);
            writer.Write(Entries.Count);
            writer.Write(BundleId ?? ComputeId());
            (long Offset, long Size) Locate(BundleFileType type)
            {
                var p = placed.FirstOrDefault(x => x.Entry.Type == type);
                return p.Entry == null ? (0, 0) : (p.Offset, p.Entry.Size);
            }
            var deps = Locate(BundleFileType.DepsJson);
            var config = Locate(BundleFileType.RuntimeConfigJson);
            writer.Write(deps.Offset); writer.Write(deps.Size);
            writer.Write(config.Offset); writer.Write(config.Size);
            writer.Write(Flags);
            foreach (var (entry, offset) in placed)
            {
                writer.Write(offset);
                writer.Write(entry.Size);
                writer.Write(entry.CompressedSize);
                writer.Write((byte)entry.Type);
                writer.Write(entry.RelativePath);
            }
            writer.Flush();

            byte[] bytes = output.ToArray();
            BitConverter.GetBytes(header).CopyTo(bytes, marker);
            return bytes;
        }

        /// <summary>A short id from the contents: a changed program gets a new id, so the host extracts it afresh.</summary>
        public string ComputeId()
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (BundleEntry e in Entries)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(e.RelativePath));
                hash.AppendData(e.Data);
            }
            return Convert.ToHexString(hash.GetHashAndReset(), 0, 10).ToLowerInvariant();
        }

        private static void Pad(Stream s, int alignment)
        {
            long extra = s.Position % alignment;
            if (extra != 0) s.Write(new byte[alignment - extra]);
        }
    }

    public sealed class CompileException : Exception
    {
        public CompileException(string message) : base(message) { }
    }
}
