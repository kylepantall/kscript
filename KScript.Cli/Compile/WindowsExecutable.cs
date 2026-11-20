using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KScript.Compile
{
    /// <summary>
    /// Gives a Windows program its own identity: the app's icon, its name and version in the file's details (what
    /// Explorer and Task Manager show), and the GUI subsystem for desktop apps so no console opens with them. Works
    /// on any computer, so Windows programs can be built from a Mac or Linux. The rebuilt resources go in a new section
    /// at the end of the launcher, which leaves everything the launcher already has where it was.
    /// </summary>
    internal static class WindowsExecutable
    {
        private const int RT_ICON = 3, RT_GROUP_ICON = 14, RT_VERSION = 16;

        /// <summary>A resource's type, name or language: a number, or a name when <see cref="Name"/> isn't null.</summary>
        internal readonly record struct ResourceKey(int Id, string Name)
        {
            public static implicit operator ResourceKey(int id) => new ResourceKey(id, null);
        }

        internal sealed record Resource(ResourceKey Type, ResourceKey Name, ResourceKey Language, byte[] Data);

        public static byte[] Customize(byte[] host, string name, string version, IReadOnlyList<IconImage> icon, bool gui)
        {
            var pe = new PeFile(host);
            List<Resource> resources = pe.ReadResources();
            resources.RemoveAll(r => r.Type.Name == null && r.Type.Id == RT_VERSION);
            resources.Add(new Resource(RT_VERSION, 1, 0x0409, VersionInfo(name, version)));
            if (icon != null && icon.Count > 0)
            {
                resources.RemoveAll(r => r.Type.Name == null && (r.Type.Id == RT_ICON || r.Type.Id == RT_GROUP_ICON));
                var group = new MemoryStream();
                var w = new BinaryWriter(group);
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)icon.Count);
                for (int i = 0; i < icon.Count; i++)
                {
                    resources.Add(new Resource(RT_ICON, i + 1, 0x0409, icon[i].Data));
                    w.Write(Icons.DirectoryEntry(icon[i]));
                    w.Write((ushort)(i + 1));
                }
                w.Flush();
                resources.Add(new Resource(RT_GROUP_ICON, 1, 0x0409, group.ToArray()));
            }
            byte[] result = pe.WithResources(resources);
            if (gui) SetSubsystem(result, 2);
            return result;
        }

        /// <summary>2 is IMAGE_SUBSYSTEM_WINDOWS_GUI, 3 the console.</summary>
        internal static void SetSubsystem(byte[] image, ushort subsystem)
        {
            int pe = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
            BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(pe + 24 + 68), subsystem);
        }

        #region Version information

        /// <summary>A VS_VERSIONINFO block with the app's name and version.</summary>
        internal static byte[] VersionInfo(string name, string version)
        {
            version ??= "1.0.0";
            ushort[] parts = new ushort[4];
            string[] given = version.Split('-', '+')[0].Split('.');
            for (int i = 0; i < 4 && i < given.Length; i++) ushort.TryParse(given[i], out parts[i]);

            var fixedInfo = new MemoryStream();
            var f = new BinaryWriter(fixedInfo);
            f.Write(0xFEEF04BDu); f.Write(0x00010000u);
            uint ms = (uint)(parts[0] << 16 | parts[1]), ls = (uint)(parts[2] << 16 | parts[3]);
            f.Write(ms); f.Write(ls); f.Write(ms); f.Write(ls);
            f.Write(0x3Fu); f.Write(0u); f.Write(0x00040004u); f.Write(1u); f.Write(0u); f.Write(0u); f.Write(0u);
            f.Flush();

            var strings = new List<byte[]>();
            foreach (var (key, value) in new[]
            {
                ("CompanyName", name), ("FileDescription", name), ("FileVersion", version), ("InternalName", name),
                ("OriginalFilename", name + ".exe"), ("ProductName", name), ("ProductVersion", version),
            })
            {
                strings.Add(Block(key, Encoding.Unicode.GetBytes(value + "\0"), (ushort)(value.Length + 1), 1, Array.Empty<byte[]>()));
            }
            byte[] table = Block("040904b0", Array.Empty<byte>(), 0, 1, strings);
            byte[] stringInfo = Block("StringFileInfo", Array.Empty<byte>(), 0, 1, new[] { table });
            byte[] translation = Block("Translation", new byte[] { 0x09, 0x04, 0xB0, 0x04 }, 4, 0, Array.Empty<byte[]>());
            byte[] varInfo = Block("VarFileInfo", Array.Empty<byte>(), 0, 1, new[] { translation });
            return Block("VS_VERSION_INFO", fixedInfo.ToArray(), 52, 0, new[] { stringInfo, varInfo });
        }

        /// <summary>One block of a version resource: length, value length, type, key, value, then children, each 4-byte aligned.</summary>
        private static byte[] Block(string key, byte[] value, ushort valueLength, ushort type, IEnumerable<byte[]> children)
        {
            var s = new MemoryStream();
            var w = new BinaryWriter(s);
            w.Write((ushort)0); w.Write(valueLength); w.Write(type);
            w.Write(Encoding.Unicode.GetBytes(key + "\0"));
            Align(s, 4);
            w.Write(value);
            foreach (byte[] child in children)
            {
                Align(s, 4);
                w.Write(child);
            }
            w.Flush();
            byte[] bytes = s.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)bytes.Length);
            return bytes;
        }

        private static void Align(Stream s, int n)
        {
            while (s.Position % n != 0) s.WriteByte(0);
        }

        #endregion

        /// <summary>The parts of a PE32+ image this needs: section table, data directories and resources.</summary>
        internal sealed class PeFile
        {
            private readonly byte[] image;
            private readonly int pe, optional, sections, sectionCount;

            public PeFile(byte[] image)
            {
                this.image = image;
                if (image.Length < 0x40 || image[0] != 'M' || image[1] != 'Z') throw new CompileException("The Windows launcher isn't a Windows executable.");
                pe = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
                if (pe <= 0 || pe + 24 > image.Length || BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(pe)) != 0x00004550)
                    throw new CompileException("The Windows launcher isn't a Windows executable.");
                optional = pe + 24;
                if (BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(optional)) != 0x20B) throw new CompileException("The Windows launcher isn't a 64-bit executable.");
                sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(pe + 6));
                sections = optional + BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(pe + 20));
            }

            private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at));
            private int Section(int i) => sections + i * 40;
            private int DataDirectory(int index) => optional + 112 + index * 8;

            internal int RvaToOffset(uint rva)
            {
                for (int i = 0; i < sectionCount; i++)
                {
                    int s = Section(i);
                    uint va = U32(s + 12), size = Math.Max(U32(s + 8), U32(s + 16));
                    if (rva >= va && rva < va + size) return (int)(rva - va + U32(s + 20));
                }
                throw new CompileException("The Windows launcher's resources point outside it.");
            }

            public List<Resource> ReadResources()
            {
                var list = new List<Resource>();
                uint rva = U32(DataDirectory(2));
                if (rva == 0 || U32(DataDirectory(2) + 4) == 0) return list;
                int root = RvaToOffset(rva);

                IEnumerable<(ResourceKey Key, uint Offset)> Entries(int dir)
                {
                    int count = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(dir + 12)) + BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(dir + 14));
                    for (int i = 0; i < count; i++)
                    {
                        uint nameField = U32(dir + 16 + i * 8), offset = U32(dir + 20 + i * 8);
                        ResourceKey key = (nameField & 0x80000000) != 0 ? new ResourceKey(0, ReadName(root + (int)(nameField & 0x7FFFFFFF))) : (int)nameField;
                        yield return (key, offset);
                    }
                }

                foreach (var type in Entries(root).Where(e => (e.Offset & 0x80000000) != 0).ToList())
                    foreach (var name in Entries(root + (int)(type.Offset & 0x7FFFFFFF)).Where(e => (e.Offset & 0x80000000) != 0).ToList())
                        foreach (var lang in Entries(root + (int)(name.Offset & 0x7FFFFFFF)).Where(e => (e.Offset & 0x80000000) == 0).ToList())
                        {
                            int entry = root + (int)lang.Offset;
                            int data = RvaToOffset(U32(entry));
                            list.Add(new Resource(type.Key, name.Key, lang.Key, image.AsSpan(data, (int)U32(entry + 4)).ToArray()));
                        }
                return list;
            }

            private string ReadName(int at) => Encoding.Unicode.GetString(image, at + 2, BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(at)) * 2);

            /// <summary>A copy of the image with <paramref name="resources"/> in a new last section, and no signature or overlay.</summary>
            public byte[] WithResources(List<Resource> resources)
            {
                uint fileAlign = U32(optional + 36), sectionAlign = U32(optional + 32);
                uint headersSize = U32(optional + 60);
                int newHeader = Section(sectionCount);
                uint firstRaw = Enumerable.Range(0, sectionCount).Select(i => U32(Section(i) + 20)).Where(p => p != 0).DefaultIfEmpty(headersSize).Min();
                if (newHeader + 40 > Math.Min(headersSize, firstRaw))
                    throw new CompileException("The Windows launcher has no room for the app's icon and details.");

                uint rawEnd = 0, vaEnd = 0;
                for (int i = 0; i < sectionCount; i++)
                {
                    int s = Section(i);
                    rawEnd = Math.Max(rawEnd, U32(s + 20) + U32(s + 16));
                    vaEnd = Math.Max(vaEnd, U32(s + 12) + Math.Max(U32(s + 8), U32(s + 16)));
                }
                uint raw = AlignUp(rawEnd, fileAlign), va = AlignUp(vaEnd, sectionAlign);
                byte[] data = Serialize(resources, va);
                uint rawSize = AlignUp((uint)data.Length, fileAlign);

                var result = new byte[raw + rawSize];
                Array.Copy(image, result, Math.Min(image.Length, (int)rawEnd));
                data.CopyTo(result, raw);

                Span<byte> h = result.AsSpan(newHeader, 40);
                Encoding.ASCII.GetBytes(".ksrc").CopyTo(h);
                BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(8), (uint)data.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(12), va);
                BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(16), rawSize);
                BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(20), raw);
                BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(36), 0x40000040); // initialized data, readable
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(pe + 6), (ushort)(sectionCount + 1));
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(optional + 8), U32(optional + 8) + rawSize);
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(optional + 56), AlignUp(va + (uint)data.Length, sectionAlign));
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(optional + 64), 0);            // checksum: not needed for programs
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(DataDirectory(2)), va);
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(DataDirectory(2) + 4), (uint)data.Length);
                BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(DataDirectory(4)), 0);          // a signature would no longer match
                return result;
            }
        }

        internal static uint AlignUp(uint value, uint alignment) => (value + alignment - 1) / alignment * alignment;

        /// <summary>Lays out a resource section (type, name and language directories, then names, entries and data) at <paramref name="baseRva"/>.</summary>
        internal static byte[] Serialize(List<Resource> resources, uint baseRva)
        {
            static IEnumerable<IGrouping<ResourceKey, T>> Ordered<T>(IEnumerable<IGrouping<ResourceKey, T>> groups) =>
                groups.OrderBy(g => g.Key.Name == null).ThenBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key.Id);

            var types = Ordered(resources.GroupBy(r => r.Type)).Select(t => (t.Key, Names: Ordered(t.GroupBy(r => r.Name))
                .Select(n => (n.Key, Langs: n.OrderBy(r => r.Language.Id).ToList())).ToList())).ToList();

            // Sizes first: every directory, then the names, then the data entries, then the data.
            int dirSize = 16 + 8 * types.Count + types.Sum(t => 16 + 8 * t.Names.Count + t.Names.Sum(n => 16 + 8 * n.Langs.Count));
            var names = resources.SelectMany(r => new[] { r.Type, r.Name, r.Language }).Where(k => k.Name != null).Select(k => k.Name).Distinct().ToList();
            var nameOffsets = new Dictionary<string, int>();
            int at = dirSize;
            foreach (string n in names) { nameOffsets[n] = at; at += 2 + n.Length * 2; }
            at = (at + 3) & ~3;
            int entriesStart = at;
            int dataStart = (entriesStart + 16 * resources.Count + 7) & ~7;

            var output = new MemoryStream();
            var w = new BinaryWriter(output);
            void Directory(int named, int ids) { w.Write(0u); w.Write(0u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)named); w.Write((ushort)ids); }
            uint KeyField(ResourceKey k) => k.Name != null ? 0x80000000u | (uint)nameOffsets[k.Name] : (uint)k.Id;

            int next = 16 + 8 * types.Count;
            Directory(types.Count(t => t.Key.Name != null), types.Count(t => t.Key.Name == null));
            var typeDirs = new List<int>();
            foreach (var t in types)
            {
                typeDirs.Add(next);
                w.Write(KeyField(t.Key)); w.Write(0x80000000u | (uint)next);
                next += 16 + 8 * t.Names.Count;
            }
            var nameDirs = new List<int>();
            foreach (var t in types)
            {
                Directory(t.Names.Count(n => n.Key.Name != null), t.Names.Count(n => n.Key.Name == null));
                foreach (var n in t.Names)
                {
                    nameDirs.Add(next);
                    w.Write(KeyField(n.Key)); w.Write(0x80000000u | (uint)next);
                    next += 16 + 8 * n.Langs.Count;
                }
            }
            int entry = entriesStart;
            var ordered = new List<Resource>();
            foreach (var t in types)
                foreach (var n in t.Names)
                {
                    Directory(n.Langs.Count(l => l.Language.Name != null), n.Langs.Count(l => l.Language.Name == null));
                    foreach (Resource r in n.Langs)
                    {
                        w.Write(KeyField(r.Language)); w.Write((uint)entry);
                        entry += 16;
                        ordered.Add(r);
                    }
                }
            foreach (string n in names) { w.Write((ushort)n.Length); w.Write(Encoding.Unicode.GetBytes(n)); }
            while (output.Position < entriesStart) w.Write((byte)0);

            int data = dataStart;
            foreach (Resource r in ordered)
            {
                w.Write(baseRva + (uint)data); w.Write((uint)r.Data.Length); w.Write(0u); w.Write(0u);
                data = (data + r.Data.Length + 7) & ~7;
            }
            while (output.Position < dataStart) w.Write((byte)0);
            foreach (Resource r in ordered)
            {
                w.Write(r.Data);
                while (output.Position % 8 != 0) w.Write((byte)0);
            }
            w.Flush();
            return output.ToArray();
        }
    }
}
