using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace KScript.Compile
{
    /// <summary>
    /// Ad-hoc signs a 64-bit Mach-O program, as Apple's linker does, so a Mac program can be built on any computer:
    /// Apple Silicon Macs won't start a program without a signature. The bundle kscript compile writes after the
    /// launcher becomes part of the <c>__LINKEDIT</c> segment, and the signature (a code directory of SHA-256 page
    /// hashes) goes at the very end.
    /// </summary>
    internal static class MacExecutable
    {
        private const uint MH_MAGIC_64 = 0xFEEDFACF;
        private const uint LC_SEGMENT_64 = 0x19, LC_CODE_SIGNATURE = 0x1D;
        private const int PageBits = 12, PageSize = 1 << PageBits, HashSize = 32;

        public static bool IsMachO(byte[] file) => file.Length >= 32 && BinaryPrimitives.ReadUInt32LittleEndian(file) == MH_MAGIC_64;

        /// <summary>Returns <paramref name="file"/> (the launcher and its bundle) with a fresh ad-hoc signature.</summary>
        public static byte[] Sign(byte[] file, string identifier)
        {
            if (!IsMachO(file)) throw new CompileException("The Mac launcher isn't a 64-bit Mac program.");
            int commands = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(16));
            int commandsSize = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(20));
            int linkedit = -1, text = -1, signature = -1;
            int at = 32;
            for (int i = 0; i < commands; i++)
            {
                uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));
                if (cmd == LC_SEGMENT_64)
                {
                    string name = Encoding.ASCII.GetString(file, at + 8, 16).TrimEnd('\0');
                    if (name == "__LINKEDIT") linkedit = at;
                    if (name == "__TEXT") text = at;
                }
                if (cmd == LC_CODE_SIGNATURE) signature = at;
                at += BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 4));
            }
            if (linkedit < 0 || text < 0) throw new CompileException("The Mac launcher has no __TEXT or __LINKEDIT segment.");

            ulong linkeditOffset = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(linkedit + 40));
            // Without a signature command, add one after the others when there is room before the first section.
            if (signature < 0)
            {
                int end = 32 + commandsSize;
                if (end + 16 > FirstSectionOffset(file, commands) || file.AsSpan(end, 16).ToArray().Any(b => b != 0))
                    throw new CompileException("The Mac launcher has no room for a signature.");
                signature = end;
                BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(end), LC_CODE_SIGNATURE);
                BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(end + 4), 16);
                BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(16), commands + 1);
                BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(20), commandsSize + 16);
            }

            // The code signature goes at the end of the file, 16-byte aligned, inside __LINKEDIT.
            int codeLimit = (file.Length + 15) & ~15;
            byte[] id = Encoding.UTF8.GetBytes(identifier + "\0");
            int pages = (codeLimit + PageSize - 1) / PageSize;
            int directoryLength = 88 + id.Length + pages * HashSize;
            int signatureLength = 12 + 8 + directoryLength;
            int signatureSize = (signatureLength + 15) & ~15;

            var output = new byte[codeLimit + signatureSize];
            file.CopyTo(output, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(signature + 8), (uint)codeLimit);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(signature + 12), (uint)signatureSize);
            ulong linkeditSize = (ulong)output.Length - linkeditOffset;
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(linkedit + 48), linkeditSize);                         // filesize
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(linkedit + 32), (linkeditSize + 0x3FFF) & ~0x3FFFUL); // vmsize, 16 KB pages

            // SuperBlob { magic, length, count, [type, offset] } then the CodeDirectory, all big-endian.
            Span<byte> s = output.AsSpan(codeLimit);
            BinaryPrimitives.WriteUInt32BigEndian(s, 0xFADE0CC0);
            BinaryPrimitives.WriteUInt32BigEndian(s.Slice(4), (uint)signatureLength);
            BinaryPrimitives.WriteUInt32BigEndian(s.Slice(8), 1);
            BinaryPrimitives.WriteUInt32BigEndian(s.Slice(12), 0);   // CSSLOT_CODEDIRECTORY
            BinaryPrimitives.WriteUInt32BigEndian(s.Slice(16), 20);
            Span<byte> cd = s.Slice(20);
            BinaryPrimitives.WriteUInt32BigEndian(cd, 0xFADE0C02);
            BinaryPrimitives.WriteUInt32BigEndian(cd.Slice(4), (uint)directoryLength);
            BinaryPrimitives.WriteUInt32BigEndian(cd.Slice(8), 0x20400);
            BinaryPrimitives.WriteUInt32BigEndian(cd.Slice(12), 0x20002);                    // CS_ADHOC | CS_LINKER_SIGNED
            BinaryPrimitives.WriteUInt32BigEndian(cd.Slice(16), (uint)(88 + id.Length));    // hashOffset
            BinaryPrimitives.WriteUInt32BigEndian(cd.Slice(20), 88);                        // identOffset
            BinaryPrimitives.WriteUInt32BigEndian(cd.Slice(24), 0);                         // special slots
            BinaryPrimitives.WriteUInt32BigEndian(cd.Slice(28), (uint)pages);
            BinaryPrimitives.WriteUInt32BigEndian(cd.Slice(32), (uint)codeLimit);
            cd[36] = HashSize;
            cd[37] = 2;                                                                      // SHA-256
            cd[39] = PageBits;
            BinaryPrimitives.WriteUInt64BigEndian(cd.Slice(64), BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(text + 40)));  // execSegBase
            BinaryPrimitives.WriteUInt64BigEndian(cd.Slice(72), BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(text + 48)));  // execSegLimit
            BinaryPrimitives.WriteUInt64BigEndian(cd.Slice(80), 1);                          // CS_EXECSEG_MAIN_BINARY
            id.CopyTo(cd.Slice(88));

            Span<byte> hashes = cd.Slice(88 + id.Length);
            for (int page = 0; page < pages; page++)
            {
                int start = page * PageSize;
                SHA256.HashData(output.AsSpan(start, Math.Min(PageSize, codeLimit - start)), hashes.Slice(page * HashSize, HashSize));
            }
            return output;
        }

        private static int FirstSectionOffset(byte[] file, int commands)
        {
            int first = int.MaxValue, at = 32;
            for (int i = 0; i < commands; i++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at)) == LC_SEGMENT_64)
                {
                    int sections = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 64));
                    for (int s = 0; s < sections; s++)
                    {
                        int offset = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 72 + s * 80 + 48));
                        if (offset > 0) first = Math.Min(first, offset);
                    }
                }
                at += BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 4));
            }
            return first;
        }
    }
}
