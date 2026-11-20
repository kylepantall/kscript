using System.IO.Compression;

using KScript.Runtime;

namespace KScript.Documents
{
    /// <summary>A PNG or JPEG image: its size and bytes, and for PDF its pixels split into colour and transparency.</summary>
    public sealed class ImageData
    {
        public string Path { get; private set; }
        public string Format { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public byte[] Bytes { get; private set; }

        public string MimeType => Format == "png" ? "image/png" : "image/jpeg";

        /// <summary>The image's colour channels: 1 (grey), 3 (RGB), or 4 (CMYK JPEG).</summary>
        public int Components { get; private set; }

        public static ImageData Load(string path)
        {
            if (!File.Exists(path)) throw new KScriptException($"There is no image at {path}.", "IO");
            return FromBytes(File.ReadAllBytes(path), path);
        }

        public static ImageData FromBytes(byte[] bytes, string path)
        {
            var image = new ImageData { Path = path, Bytes = bytes };
            if (bytes.Length > 24 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
            {
                image.Format = "png";
                image.Width = BigEndian(bytes, 16);
                image.Height = BigEndian(bytes, 20);
                image.Components = bytes[25] switch { 0 or 4 => 1, _ => 3 };
                return image;
            }
            if (bytes.Length > 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                image.Format = "jpeg";
                ReadJpegSize(bytes, image);
                return image;
            }
            throw new KScriptException($"{System.IO.Path.GetFileName(path)} isn't a PNG or JPEG image. Documents can hold PNG and JPEG images.", "Format");
        }

        private static int BigEndian(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

        private static void ReadJpegSize(byte[] b, ImageData image)
        {
            int i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF)
                {
                    i++;
                    continue;
                }
                byte marker = b[i + 1];
                int length = (b[i + 2] << 8) | b[i + 3];
                // SOF0-SOF15 hold the size, except DHT (C4), JPG (C8) and DAC (CC).
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    image.Height = (b[i + 5] << 8) | b[i + 6];
                    image.Width = (b[i + 7] << 8) | b[i + 8];
                    image.Components = b[i + 9];
                    return;
                }
                i += 2 + length;
            }
            throw new KScriptException($"{System.IO.Path.GetFileName(image.Path)} is a JPEG without a size, so it may be damaged.", "Format");
        }

        /// <summary>
        /// A PNG's pixels for a PDF: colour (grey or RGB, 8 bits) and transparency (or null), each zlib-compressed.
        /// Palette images become RGB.
        /// </summary>
        internal (byte[] Colour, byte[] Alpha, int Components) DecodePng()
        {
            byte[] b = Bytes;
            int bitDepth = b[24], colourType = b[25], interlace = b[28];
            if (interlace != 0) throw new KScriptException($"{System.IO.Path.GetFileName(Path)} is an interlaced PNG, which PDF output can't use. Save it again without interlacing.", "Format");
            if (bitDepth != 8 && !(colourType == 3 && bitDepth <= 8) && !(colourType == 0 && bitDepth <= 8))
                throw new KScriptException($"{System.IO.Path.GetFileName(Path)} has {bitDepth}-bit colour; PDF output uses 8-bit PNGs. Save it again as 8-bit.", "Format");

            var data = new MemoryStream();
            byte[] palette = null, paletteAlpha = null;
            int pos = 8;
            while (pos + 8 <= b.Length)
            {
                int len = BigEndian(b, pos);
                string type = System.Text.Encoding.ASCII.GetString(b, pos + 4, 4);
                if (type == "IDAT") data.Write(b, pos + 8, len);
                else if (type == "PLTE") palette = b.Skip(pos + 8).Take(len).ToArray();
                else if (type == "tRNS") paletteAlpha = b.Skip(pos + 8).Take(len).ToArray();
                else if (type == "IEND") break;
                pos += 12 + len;
            }
            data.Position = 0;
            byte[] raw;
            using (var z = new ZLibStream(data, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                z.CopyTo(output);
                raw = output.ToArray();
            }

            int channels = colourType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
            int bitsPerPixel = channels * bitDepth;
            int stride = (Width * bitsPerPixel + 7) / 8;
            int bpp = Math.Max(1, bitsPerPixel / 8);
            byte[] pixels = Unfilter(raw, stride, bpp);

            int outChannels = colourType == 0 || colourType == 4 ? 1 : 3;
            bool hasAlpha = colourType == 4 || colourType == 6 || (colourType == 3 && paletteAlpha != null);
            var colour = new byte[Width * Height * outChannels];
            byte[] alpha = hasAlpha ? new byte[Width * Height] : null;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int p = y * Width + x;
                    int row = y * stride;
                    switch (colourType)
                    {
                        case 0:
                        {
                            int v = Sample(pixels, row, x, bitDepth);
                            colour[p] = (byte)(v * 255 / ((1 << bitDepth) - 1));
                            break;
                        }
                        case 2:
                            Buffer.BlockCopy(pixels, row + x * 3, colour, p * 3, 3);
                            break;
                        case 3:
                        {
                            int index = Sample(pixels, row, x, bitDepth);
                            if (palette != null && index * 3 + 2 < palette.Length) Buffer.BlockCopy(palette, index * 3, colour, p * 3, 3);
                            if (alpha != null) alpha[p] = index < paletteAlpha.Length ? paletteAlpha[index] : (byte)255;
                            break;
                        }
                        case 4:
                            colour[p] = pixels[row + x * 2];
                            alpha[p] = pixels[row + x * 2 + 1];
                            break;
                        default:
                            Buffer.BlockCopy(pixels, row + x * 4, colour, p * 3, 3);
                            alpha[p] = pixels[row + x * 4 + 3];
                            break;
                    }
                }
            }
            return (Compress(colour), alpha == null ? null : Compress(alpha), outChannels);
        }

        private static int Sample(byte[] pixels, int row, int x, int bitDepth)
        {
            if (bitDepth == 8) return pixels[row + x];
            int bit = x * bitDepth;
            int value = pixels[row + bit / 8];
            int shift = 8 - bitDepth - bit % 8;
            return (value >> shift) & ((1 << bitDepth) - 1);
        }

        private static byte[] Unfilter(byte[] raw, int stride, int bpp)
        {
            int rows = raw.Length / (stride + 1);
            var result = new byte[rows * stride];
            for (int y = 0; y < rows; y++)
            {
                int filter = raw[y * (stride + 1)];
                int src = y * (stride + 1) + 1;
                int dst = y * stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? result[dst + x - bpp] : 0;
                    int up = y > 0 ? result[dst - stride + x] : 0;
                    int c = x >= bpp && y > 0 ? result[dst - stride + x - bpp] : 0;
                    int v = raw[src + x];
                    v += filter switch
                    {
                        1 => a,
                        2 => up,
                        3 => (a + up) / 2,
                        4 => Paeth(a, up, c),
                        _ => 0,
                    };
                    result[dst + x] = (byte)v;
                }
            }
            return result;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        internal static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data, 0, data.Length);
            return output.ToArray();
        }
    }
}
