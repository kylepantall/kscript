using System.Text.Json;
using KScript.Runtime;

namespace KScript.Net
{
    // The read buffer belongs to the connection: a read can contain several newline-delimited frames.
    internal sealed class JsonLineFrames
    {
        private const int MaxBytes = 256 * 1024;
        private readonly byte[] buffer = new byte[8192];
        private int offset, available;

        internal static void Write(Stream stream, Dictionary<string, object> value, CancellationToken cancellation)
        {
            using var frame = new MemoryStream();
            Values.WriteJson(frame, value, maxBytes: MaxBytes);
            frame.WriteByte((byte)'\n');
            stream.WriteAsync(frame.GetBuffer().AsMemory(0, (int)frame.Length), cancellation).AsTask().GetAwaiter().GetResult();
        }

        internal JsonDocument Read(Stream stream, CancellationToken cancellation)
        {
            using var frame = new MemoryStream();
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (offset == available)
                {
                    available = stream.ReadAsync(buffer, cancellation).AsTask().GetAwaiter().GetResult();
                    offset = 0;
                    if (available == 0) throw new EndOfStreamException("The local service disconnected before sending a complete frame.");
                }
                int newline = Array.IndexOf(buffer, (byte)'\n', offset, available - offset);
                int end = newline < 0 ? available : newline;
                int length = end - offset;
                if (frame.Length + length > MaxBytes) throw new InvalidDataException("The incoming JSON frame exceeds 256 KiB.");
                frame.Write(buffer, offset, length);
                offset = end;
                if (newline >= 0)
                {
                    offset++;
                    return JsonDocument.Parse(frame.GetBuffer().AsMemory(0, (int)frame.Length));
                }
            }
        }
    }
}
