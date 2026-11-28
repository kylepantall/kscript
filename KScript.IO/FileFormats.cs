using System.Text;

using KScript.Metadata;
using KScript.Runtime;

using Microsoft.VisualBasic.FileIO;

namespace KScript.IO
{
    internal static class FileFormats
    {
        internal static void Register(KScriptPackage.PackageBuilder p)
        {
            p.Command("read_bytes", "path", "Reads every byte of a file as a base64 value, including binary data.", "base64",
                c => Base64Type.EncodeBytes(KScript.Runtime.Dependencies.DependencyTrace.ReadBytes(IOPackage.PathOf(c.Need(0), c))));
            p.Command("write_bytes", "path, bytes: base64", "Writes the exact bytes held by a base64 value.", KType.Null, c =>
            {
                c.Host.CheckWrite("@write_bytes()");
                string path = IOPackage.PathOf(c.Need(0), c);
                if (!Base64Type.TryDecodeBytes(c.Need(1), out byte[] bytes)) throw c.Error("bytes must be a base64 value.", "Type");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, bytes);
                return null;
            });
            p.Command("write_atomic", "path, value", "Replaces a file atomically with text or the bytes of a base64 value.", KType.Null, c =>
            {
                c.Host.CheckWrite("@write_atomic()");
                string path = IOPackage.PathOf(c.Need(0), c);
                object value = c.Need(1);
                if (!Base64Type.TryDecodeBytes(value, out byte[] bytes)) bytes = Encoding.UTF8.GetBytes(Values.ToStr(value));
                Atomic(path, bytes);
                return null;
            });
            p.Command("with_temp_file", "use: fn", "Calls a function with a temporary file, then removes the file when the call ends.", KType.Any,
                c => WithTemporary(c, file: true));
            p.Command("with_temp_folder", "use: fn", "Calls a function with a temporary folder, then removes it when the call ends.", KType.Any,
                c => WithTemporary(c, file: false));
            p.Command("read_csv", "path, header?: bool, as?: type", "Reads CSV as rows of maps (or rows of lists with header: false). An optional type validates the rows.", KType.List, ReadCsv);
            p.Command("write_csv", "path, rows: list, headers?: list", "Writes maps as CSV with a header, or lists as rows. Optional headers select and order columns.", KType.Null, WriteCsv);
            p.Examples(
                ("@read_bytes", "<do>@write_text('sample.txt', 'Hello')</do>\n<echo>{ @read_bytes('sample.txt') }</echo>"),
                ("@write_bytes", "<let name=\"data\" type=\"base64\" value=\"'AAE='\" />\n<do>@write_bytes('sample.bin', $data)</do>\n<echo>{ @read_bytes('sample.bin') }</echo>"),
                ("@write_atomic", "<do>@write_atomic('notes.txt', 'Saved')</do>\n<echo>{ @read_text('notes.txt') }</echo>"),
                ("@with_temp_file", "<echo>{ @with_temp_file(fn(file) => @exists($file)) }</echo>"),
                ("@with_temp_folder", "<echo>{ @with_temp_folder(fn(folder) => @exists($folder)) }</echo>"),
                ("@read_csv", "<do>@write_csv('people.csv', [{ name: 'Joan' }])</do>\n<echo>{ @read_csv('people.csv')->first().name }</echo>"),
                ("@write_csv", "<do>@write_csv('people.csv', [{ name: 'Joan' }])</do>\n<echo>{ @read_text('people.csv') }</echo>"));
        }

        private static void Atomic(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = Path.Combine(Path.GetDirectoryName(path), ".kscript-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                    stream.Write(bytes);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        private static object WithTemporary(CallContext call, bool file)
        {
            call.Host.CheckWrite($"@{call.Name}()");
            ICallable use = call.Fn(0);
            string folder = Path.Combine(call.Host.FileRoot ?? Path.GetTempPath(), "kscript-" + Guid.NewGuid().ToString("N"));
            call.Host.CheckPath(folder);
            Directory.CreateDirectory(folder);
            try
            {
                string path = file ? Path.Combine(folder, "file") : folder;
                if (file) File.Create(path).Dispose();
                return call.Call(use, new FileValue(path));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
        }

        private static object ReadCsv(CallContext call)
        {
            string path = IOPackage.PathOf(call.Need(0), call);
            using var parser = new TextFieldParser(path) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            parser.SetDelimiters(",");
            var rows = new List<object>();
            try
            {
                string[] header = call.Bool(1, true) && !parser.EndOfData ? parser.ReadFields() : null;
                if (header != null && (header.Any(string.IsNullOrEmpty) || header.Distinct(StringComparer.Ordinal).Count() != header.Length))
                    throw call.Error("CSV headers must be nonempty and unique.", "Format");
                while (!parser.EndOfData)
                {
                    long line = parser.LineNumber;
                    string[] fields = parser.ReadFields();
                    if (header == null)
                        rows.Add(fields.Cast<object>().ToList());
                    else
                    {
                        if (fields.Length != header.Length)
                            throw call.Error($"CSV row on line {line} has {fields.Length} fields and the header has {header.Length}.", "Format");
                        rows.Add(header.Zip(fields).ToDictionary(field => field.First, field => (object)field.Second, StringComparer.Ordinal));
                    }
                }
            }
            catch (MalformedLineException ex)
            {
                throw call.Error($"Malformed CSV near line {ex.LineNumber}.", "Format");
            }
            return Shapes.MapJson(call, rows, 2);
        }

        private static object WriteCsv(CallContext call)
        {
            call.Host.CheckWrite("@write_csv()");
            string path = IOPackage.PathOf(call.Need(0), call);
            List<object> rows = call.List(1);
            string[] headers = call.Has(2) ? call.List(2).Select(Values.ToStr).ToArray() : null;
            if (headers == null && rows.FirstOrDefault() is Dictionary<string, object> first)
                headers = first.Keys.ToArray();
            if (headers != null && (headers.Length == 0 || headers.Any(string.IsNullOrEmpty) || headers.Distinct(StringComparer.Ordinal).Count() != headers.Length))
                throw call.Error("CSV headers must be nonempty and unique.", "Argument");
            var text = new StringBuilder();
            if (headers != null) Append(headers);
            foreach (object row in rows)
            {
                switch (row)
                {
                    case Dictionary<string, object> map when headers != null:
                        Append(headers.Select(key => Values.ToStr(map.GetValueOrDefault(key))));
                        break;
                    case List<object> fields when headers == null || fields.Count == headers.Length:
                        Append(fields.Select(Values.ToStr));
                        break;
                    default:
                        throw call.Error("Each CSV row must match the header or be a list of cells.", "Argument");
                }
            }
            Atomic(path, Encoding.UTF8.GetBytes(text.ToString()));
            return null;

            void Append(IEnumerable<string> fields) =>
                text.AppendJoin(',', fields.Select(field => "\"" + field.Replace("\"", "\"\"") + "\"")).Append("\r\n");
        }
    }
}
