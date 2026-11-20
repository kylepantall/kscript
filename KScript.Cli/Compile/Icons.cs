using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KScript.Compile
{
    /// <summary>One picture in an icon: its size and its bytes (PNG, or a Windows bitmap when <see cref="Png"/> is false).</summary>
    internal sealed record IconImage(int Width, int Height, byte[] Data, bool Png, ushort BitCount = 32);

    /// <summary>
    /// Reads an app icon (.png, .ico or .icns) and writes it in the form each platform wants: an .ico's pictures for a
    /// Windows executable, an .icns for a Mac app, and a .png for Linux. Pictures are carried over as they are, never
    /// resized, so a 1024 px PNG makes a sharp icon everywhere.
    /// </summary>
    internal static class Icons
    {
        private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static List<IconImage> Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            List<IconImage> images = IsPng(data) ? new List<IconImage> { FromPng(data) }
                : data.Length >= 4 && Encoding.ASCII.GetString(data, 0, 4) == "icns" ? FromIcns(data)
                : data.Length >= 6 && BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)) == 1 && BinaryPrimitives.ReadUInt16LittleEndian(data) == 0 ? FromIco(data)
                : null;
            if (images == null || images.Count == 0)
                throw new CompileException($"app.icon: {Path.GetFileName(path)} isn't a PNG, .ico or .icns picture.");
            return images;
        }

        internal static bool IsPng(byte[] data) => data.Length > 24 && data.AsSpan(0, 8).SequenceEqual(PngMagic);

        internal static IconImage FromPng(byte[] png) =>
            new IconImage(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)), png, true);

        private static List<IconImage> FromIco(byte[] ico)
        {
            var images = new List<IconImage>();
            int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
            for (int i = 0; i < count; i++)
            {
                int at = 6 + i * 16;
                if (at + 16 > ico.Length) break;
                int size = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(at + 8));
                int offset = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(at + 12));
                if (offset < 0 || size <= 0 || offset + size > ico.Length) continue;
                byte[] data = ico.AsSpan(offset, size).ToArray();
                int w = ico[at] == 0 ? 256 : ico[at], h = ico[at + 1] == 0 ? 256 : ico[at + 1];
                images.Add(IsPng(data) ? FromPng(data) : new IconImage(w, h, data, false, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(at + 6))));
            }
            return images;
        }

        private static List<IconImage> FromIcns(byte[] icns)
        {
            var images = new List<IconImage>();
            for (int at = 8; at + 8 <= icns.Length;)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(icns.AsSpan(at + 4));
                if (length < 8 || at + length > icns.Length) break;
                byte[] data = icns.AsSpan(at + 8, length - 8).ToArray();
                if (IsPng(data)) images.Add(FromPng(data));
                at += length;
            }
            return images;
        }

        /// <summary>The largest picture, for formats that hold one.</summary>
        public static IconImage Largest(IEnumerable<IconImage> images) => images.OrderByDescending(i => i.Width * i.Height).ThenByDescending(i => i.Png).First();

        /// <summary>An .icns holding the PNG pictures, each under the type for its size.</summary>
        public static byte[] ToIcns(IEnumerable<IconImage> images)
        {
            var chunks = new List<byte[]>();
            var used = new HashSet<string>();
            foreach (IconImage image in images.Where(i => i.Png).OrderByDescending(i => i.Width))
            {
                string type = image.Width switch
                {
                    >= 1024 => "ic10",
                    >= 512 => "ic09",
                    >= 256 => "ic08",
                    >= 128 => "ic07",
                    >= 64 => "ic12",
                    >= 32 => "ic11",
                    _ => "icp4",
                };
                if (!used.Add(type)) continue;
                var chunk = new byte[8 + image.Data.Length];
                Encoding.ASCII.GetBytes(type).CopyTo(chunk, 0);
                BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(4), chunk.Length);
                image.Data.CopyTo(chunk, 8);
                chunks.Add(chunk);
            }
            if (chunks.Count == 0) throw new CompileException("app.icon: a Mac icon needs a PNG picture. Use a .png or an .icns.");
            var icns = new byte[8 + chunks.Sum(c => c.Length)];
            Encoding.ASCII.GetBytes("icns").CopyTo(icns, 0);
            BinaryPrimitives.WriteInt32BigEndian(icns.AsSpan(4), icns.Length);
            int offset = 8;
            foreach (byte[] c in chunks) { c.CopyTo(icns, offset); offset += c.Length; }
            return icns;
        }

        /// <summary>The PNG for Linux, or null when the icon has none.</summary>
        public static byte[] ToPng(IEnumerable<IconImage> images) => images.Where(i => i.Png).OrderByDescending(i => i.Width).FirstOrDefault()?.Data;

        /// <summary>The 14-byte GRPICONDIR entry (or the first 12 bytes of an ICONDIRENTRY) for a picture.</summary>
        internal static byte[] DirectoryEntry(IconImage image)
        {
            var e = new byte[12];
            e[0] = (byte)(image.Width >= 256 ? 0 : image.Width);
            e[1] = (byte)(image.Height >= 256 ? 0 : image.Height);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(6), image.BitCount);
            BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(8), image.Data.Length);
            return e;
        }
    }
}
