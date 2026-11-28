using System.Text;

using KScript.Metadata;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.IO
{
    /// <summary>A file or folder path with fields (<c>$file.name</c>, <c>.extension</c>, <c>.size</c>) and methods (<c>->read_text()</c>).</summary>
    public sealed class FileValue : IKObject
    {
        public FileValue(string path) => Path = System.IO.Path.GetFullPath(path);

        public string Path { get; }

        public string TypeName => KType.File;

        public string ToText() => Path;

        public override string ToString() => Path;

        public override bool Equals(object obj) => obj is FileValue f && f.Path == Path;

        public override int GetHashCode() => Path.GetHashCode();

        /// <summary>Fields go through the package (<see cref="Fields"/>), which checks the script may read the path first.</summary>
        public bool TryGetMember(string name, out object value)
        {
            value = null;
            return false;
        }

        internal object Field(string name) => name switch
        {
            "path" => Path,
            "name" => System.IO.Path.GetFileName(Path),
            "stem" => System.IO.Path.GetFileNameWithoutExtension(Path),
            "extension" => Extension(),
            "directory" => new FileValue(System.IO.Path.GetDirectoryName(Path) ?? Path),
            "exists" => File.Exists(Path) || Directory.Exists(Path),
            "is_file" => File.Exists(Path),
            "is_folder" => Directory.Exists(Path),
            "size" => File.Exists(Path) ? (double)new FileInfo(Path).Length : 0d,
            "modified" => File.GetLastWriteTime(Path),
            "created" => File.GetCreationTime(Path),
            _ => null,
        };

        private string Extension() => System.IO.Path.GetExtension(Path).TrimStart('.');

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            call.Host.CheckPath(Path);
            result = name switch
            {
                "read_text" => KScript.Runtime.Dependencies.DependencyTrace.ReadText(Path),
                "read_lines" => KScript.Runtime.Dependencies.DependencyTrace.ReadLines(Path).Cast<object>().ToList(),
                "join" => Checked(call, new FileValue(IOPackage.JoinPath(call.Args.Prepend(Path)))),
                "relative_to" => System.IO.Path.GetRelativePath(IOPackage.PathOf(call.Arg(0), call), Path),
                "to_string" => Path,
                _ => NoMethod,
            };
            if (ReferenceEquals(result, NoMethod))
            {
                result = null;
                return false;
            }
            return true;
        }

        private static readonly object NoMethod = new object();

        private static FileValue Checked(CallContext call, FileValue file)
        {
            call.Host.CheckPath(file.Path);
            return file;
        }

        internal static readonly (string Name, string Type, string Description)[] Fields =
        {
            ("path", KType.String, "The full path as text."),
            ("name", KType.String, "The file name with its extension: report.pdf."),
            ("stem", KType.String, "The file name without its extension: report."),
            ("extension", KType.String, "The extension without the dot: pdf (empty if there isn't one)."),
            ("directory", KType.File, "The folder the file is in."),
            ("exists", KType.Bool, "True when a file or folder is there."),
            ("is_file", KType.Bool, "True when it is a file."),
            ("is_folder", KType.Bool, "True when it is a folder."),
            ("size", KType.Number, "The size in bytes (0 for folders)."),
            ("modified", KType.Date, "When the file was last changed."),
            ("created", KType.Date, "When the file was created."),
        };

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("read_text", "", "The file's text.", KType.String),
            ("read_lines", "", "The file's lines.", KType.List),
            ("join", "parts...", "A file value for the joined path. Parts can be separate arguments or a list, as in $dir->join(['a', 'b.txt']).", KType.File),
            ("relative_to", "folder", "The path relative to a folder.", KType.String),
        };
    }

    /// <summary>KScript.IO: reading and writing files and folders.</summary>
    public sealed class IOPackage : KScriptPackage
    {
        public override string Name => "KScript.IO";

        public override string Description => "Files and folders: read, write, list, move, copy and delete.";

        /// <summary>A path argument (text or a file value) resolved against the script's folder.</summary>
        public static string PathOf(object value, CallContext c) => Resolve(value, c.Host, c.Module?.Directory ?? c.Host.WorkingDirectory);

        internal static string JoinPath(IEnumerable<object> parts)
        {
            static IEnumerable<string> Segments(object part) => part is List<object> list
                ? list.SelectMany(Segments)
                : new[] { part is FileValue file ? file.Path : Values.ToStr(part) };
            return Path.Combine(parts.SelectMany(Segments).ToArray());
        }

        private static string Resolve(object value, KScriptHost host, string baseDirectory)
        {
            if (value is FileValue f)
            {
                host.CheckPath(f.Path);
                return f.Path;
            }
            return host.ResolvePath(Values.ToStr(value), baseDirectory);
        }

        protected override void Register(PackageBuilder p)
        {
            foreach (var f in FileValue.Fields)
                p.Field(KType.File, f.Name, f.Type, f.Description, (t, i) =>
                {
                    i.Host.CheckPath(((FileValue)t).Path);
                    return ((FileValue)t).Field(f.Name);
                });
            foreach (var m in FileValue.Methods)
                p.Method(KType.File, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((FileValue)t).TryCallMethod(c, m.Name, out object r) ? r : null);

            p.Command("file", "path", "A file value for a path, with fields like .name, .extension and .size, and methods like ->read_text().", KType.File, c => new FileValue(PathOf(c.Need(0), c)),
                "<let name=\"report\" value=\"@file('out/report.pdf')\" />");
            p.Command("read_text", "path", "The text of a file. Relative paths are from the script's folder.", KType.String, c => KScript.Runtime.Dependencies.DependencyTrace.ReadText(PathOf(c.Need(0), c)),
                "<do>@write_text('config.json', '{ \"port\": 8080 }')</do>\n<let name=\"settings\" value=\"@read_text('config.json')->json()\" />\n<echo>$settings.port</echo>");
            p.Command("read_lines", "path", "The lines of a file.", KType.List, c => KScript.Runtime.Dependencies.DependencyTrace.ReadLines(PathOf(c.Need(0), c)).Cast<object>().ToList());
            p.Command("read_json", "path, as?: type", "A JSON file read into maps and lists. With as: a <type>, the data is checked and turned into records.", KType.Any, c => Shapes.MapJson(c, Values.ParseJson(KScript.Runtime.Dependencies.DependencyTrace.ReadText(PathOf(c.Need(0), c))), 1));
            p.Command("read_yaml", "path", "A YAML file read into maps and lists.", KType.Any, c =>
            {
                string path = PathOf(c.Need(0), c);
                return Yaml.Parse(KScript.Runtime.Dependencies.DependencyTrace.ReadText(path), System.IO.Path.GetFileName(path));
            });
            p.Command("read_env", "path", "A .env file (NAME=value lines) read into a map. ${NAME} uses earlier lines or the environment.", KType.Map, c =>
            {
                string path = PathOf(c.Need(0), c);
                var map = new Dictionary<string, object>();
                foreach (var pair in DotEnv.Parse(KScript.Runtime.Dependencies.DependencyTrace.ReadText(path), c.Host.EnvironmentVariable, System.IO.Path.GetFileName(path))) map[pair.Key] = pair.Value;
                return map;
            });
            p.Command("write_text", "path, text", "Writes text to a file, replacing it. Folders are created as needed.", KType.Null, c => Write(c, c.Str(1), append: false));
            p.Command("append_text", "path, text", "Adds text to the end of a file.", KType.Null, c => Write(c, c.Str(1), append: true));
            p.Command("write_json", "path, value", "Writes a value to a file as indented JSON.", KType.Null, c => Write(c, Values.ToJson(c.Arg(1), indented: true), append: false));
            p.Command("write_yaml", "path, value", "Writes a value to a file as YAML.", KType.Null, c => Write(c, Yaml.Write(c.Arg(1)), append: false));
            p.Command("exists", "path", "True if a file or folder is at the path.", KType.Bool, c =>
            {
                string path = PathOf(c.Need(0), c);
                return File.Exists(path) || Directory.Exists(path);
            });
            p.Command("is_file", "path", "True if a file is at the path.", KType.Bool, c => File.Exists(PathOf(c.Need(0), c)));
            p.Command("is_folder", "path", "True if a folder is at the path.", KType.Bool, c => Directory.Exists(PathOf(c.Need(0), c)));
            p.Command("files", "folder, pattern?, recursive?: bool", "The files in a folder, as file values. pattern is a wildcard like '*.txt'.", KType.List,
                c => List(c, Directory.EnumerateFiles), "<for each=\"f\" in=\"@files('.', '*.log')\"><echo>{$f.name}</echo></for>");
            p.Command("folders", "folder, pattern?, recursive?: bool", "The folders in a folder, as file values.", KType.List, c => List(c, Directory.EnumerateDirectories));
            p.Command("join_path", "base, parts...", "Joins a folder and the parts below it with the right separator. Parts may be a list: @join_path(base: $folder, ['archive', $name]).", KType.String,
                c => JoinPath(c.Args));
            p.Command("full_path", "path", "The absolute path.", KType.String, c => PathOf(c.Need(0), c));
            p.Command("cwd", "", "The folder kscript was run from.", KType.String, c => c.Host.WorkingDirectory);
            p.Command("home", "", "The user's home folder.", KType.String, c => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            p.Command("temp_folder", "", "The folder for temporary files.", KType.String, c => System.IO.Path.GetTempPath());
            p.Command("known_folder", "name: home|desktop|documents|downloads|music|pictures|videos|appdata|temp", "A standard folder: home, desktop, documents, downloads, music, pictures, videos, appdata or temp.", KType.String, c => KnownFolder(c, c.Str(0)));

            p.Command("move", "from, to, overwrite?: bool", "Moves (or renames) a file or folder, and returns it as a file value. to is a full path, or an existing folder to move it into. overwrite: true replaces a file that is already there.", KType.File,
                c => Transfer(c, move: true));
            p.Command("copy", "from, to, overwrite?: bool", "Copies a file or folder, and returns the copy as a file value. to is a full path, or an existing folder to copy it into. overwrite: true replaces files that are already there.", KType.File,
                c => Transfer(c, move: false));
            p.Command("delete", "path", "Deletes a file, or a folder and everything in it. Does nothing if it isn't there.", KType.Null, c =>
            {
                c.Host.CheckWrite("@delete()");
                string path = PathOf(c.Need(0), c);
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return null;
            });
            p.Command("create_folder", "path", "Creates a folder (and any folders above it), and returns it as a file value. Does nothing if it is already there.", KType.File, c =>
            {
                c.Host.CheckWrite("@create_folder()");
                string path = PathOf(c.Need(0), c);
                Directory.CreateDirectory(path);
                return new FileValue(path);
            });
            FileFormats.Register(p);
            RegisterExamples(p);
            p.AgentNotes(
                ("@join_path", new AgentNotes(prefer: "@join_path(base: $dir, ['reports', 'a.txt']) over joining text with / or \\.")),
                ("@read_json", new AgentNotes(prefer: "@read_json($path, as: Type) checks the data's shape.", pitfall: "A missing file throws FileNotFound: check @exists($path) or catch it.")),
                ("@read_text", new AgentNotes(pitfall: "A missing file throws FileNotFound: check @exists($path) or catch it.")),
                ("@known_folder", new AgentNotes(prefer: "@known_folder(downloads) over building paths from $env.HOME.")));
        }

        /// <summary>Usage examples, shown in hover and run by kscript docs.</summary>
        private static void RegisterExamples(PackageBuilder p)
        {
            p.Examples(
                ("@read_lines", "<do>@write_text('todo.txt', 'milk' + @char_of(10) + 'eggs')</do>\n<echo>{ @read_lines('todo.txt') }</echo>"),
                ("@read_json", "<do>@write_json('people.json', [{ name: 'Joan', age: 82 }])</do>\n<echo>{ @read_json('people.json')->first().name }</echo>"),
                ("@read_yaml", "<do>@write_text('app.yaml', 'name: Shop' + @char_of(10) + 'port: 8080')</do>\n<echo>{ @read_yaml('app.yaml').port }</echo>"),
                ("@read_env", "<do>@write_text('.env', 'PORT=8080' + @char_of(10) + 'URL=http://localhost:${PORT}')</do>\n<echo>{ @read_env('.env').URL }</echo>"),
                ("@write_text", "<do>@write_text('hello.txt', 'Hello!')</do>\n<echo>{ @read_text('hello.txt') }</echo>"),
                ("@append_text", "<do>@write_text('log.txt', 'one;'); @append_text('log.txt', 'two;')</do>\n<echo>{ @read_text('log.txt') }</echo>"),
                ("@write_json", "<do>@write_json('settings.json', { theme: 'dark', size: 14 })</do>\n<echo>{ @read_text('settings.json') }</echo>"),
                ("@write_yaml", "<do>@write_yaml('settings.yaml', { theme: 'dark', size: 14 })</do>\n<echo>{ @read_text('settings.yaml') }</echo>"),
                ("@exists", "<do>@write_text('a.txt', 'a')</do>\n<echo>{ @exists('a.txt') } { @exists('b.txt') }</echo>"),
                ("@is_file", "<do>@write_text('a.txt', 'a')</do>\n<echo>{ @is_file('a.txt') } { @is_file('.') }</echo>"),
                ("@is_folder", "<do>@create_folder('photos')</do>\n<echo>{ @is_folder('photos') }</echo>"),
                ("@folders", "<do>@create_folder('2025'); @create_folder('2026')</do>\n<echo>{ @folders('.')->map(fn(f) => $f.name)->sort() }</echo>"),
                ("@move", "<do>@write_text('report.txt', 'Q3')</do>\n<do>@create_folder('archive')</do>\n<let name=\"file\" value=\"@file('report.txt')\" />\n<do>@move($file, @join_path('archive', $file.name))</do>\n<echo>{ @exists('archive/report.txt') } { @exists('report.txt') }</echo>"),
                ("@copy", "<do>@write_text('a.txt', 'a')</do>\n<echo>{ @copy('a.txt', 'b.txt').name }</echo>"),
                ("@delete", "<do>@write_text('old.txt', 'x')</do>\n<do>@delete('old.txt')</do>\n<echo>{ @exists('old.txt') }</echo>"),
                ("@create_folder", "<echo>{ @create_folder('out/reports').name }</echo>"),
                ("@join_path", "@join_path('reports', '2026', 'november.pdf')"),
                ("@full_path", "@full_path('notes.txt')->ends_with('notes.txt')"),
                ("@cwd", "@cwd()->count() > 0"),
                ("@home", "@home()->count() > 0"),
                ("@temp_folder", "@temp_folder()->count() > 0"),
                ("@known_folder", "@known_folder(downloads)->count() > 0"),
                ("file.path", "@file('notes.txt').path->ends_with('notes.txt')"),
                ("file.name", "@file('reports/q3.pdf').name"),
                ("file.stem", "@file('reports/q3.pdf').stem"),
                ("file.extension", "@file('reports/q3.pdf').extension"),
                ("file.directory", "@file('reports/q3.pdf').directory.name"),
                ("file.exists", "@file('missing.txt').exists"),
                ("file.is_file", "<do>@write_text('a.txt', 'a')</do>\n<echo>{ @file('a.txt').is_file }</echo>"),
                ("file.is_folder", "<do>@create_folder('photos')</do>\n<echo>{ @file('photos').is_folder }</echo>"),
                ("file.size", "<do>@write_text('a.txt', 'hello')</do>\n<echo>{ @file('a.txt').size } bytes</echo>"),
                ("file.modified", "<do>@write_text('a.txt', 'hello')</do>\n<echo>{ @file('a.txt').modified.year ge 2026 }</echo>"),
                ("file.created", "<do>@write_text('a.txt', 'hello')</do>\n<echo>{ @file('a.txt').created.year ge 2000 }</echo>"),
                ("file->read_text", "<do>@write_text('a.txt', 'hello')</do>\n<echo>{ @file('a.txt')->read_text() }</echo>"),
                ("file->read_lines", "<do>@write_text('a.txt', 'one' + @char_of(10) + 'two')</do>\n<echo>{ @file('a.txt')->read_lines() }</echo>"),
                ("file->join", "@file('reports')->join('2026', 'q3.pdf').name"),
                ("file->relative_to", "@file('reports/2026/q3.pdf')->relative_to('reports')"));
        }

        private static object Write(CallContext c, string text, bool append)
        {
            c.Host.CheckWrite($"@{c.Name}()");
            string path = PathOf(c.Need(0), c);
            EnsureFolder(path);
            // No encoding argument: naming UTF-8 here would start a new file with a byte order mark, which @write_text never does.
            if (append) File.AppendAllText(path, text);
            else File.WriteAllText(path, text);
            return null;
        }

        private static void EnsureFolder(string path)
        {
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        private static object List(CallContext c, Func<string, string, SearchOption, IEnumerable<string>> enumerate)
        {
            string folder = PathOf(c.Need(0), c);
            if (!Directory.Exists(folder)) throw new KScriptException($"There is no folder at '{folder}'.", "FileNotFound");
            string pattern = c.Has(1) && c.Arg(1) != null ? c.Str(1) : "*";
            var option = c.Bool(2) ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return enumerate(folder, pattern, option).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => (object)new FileValue(x)).ToList();
        }

        private static object Transfer(CallContext c, bool move)
        {
            c.Host.CheckWrite($"@{c.Name}()");
            string from = PathOf(c.Need(0), c);
            string to = PathOf(c.Need(1), c);
            bool overwrite = c.Bool(2);
            bool isFolder = Directory.Exists(from);
            if (!isFolder && !File.Exists(from)) throw new KScriptException($"There is no file or folder at '{from}'.", "FileNotFound");
            if (Directory.Exists(to)) to = System.IO.Path.Combine(to, System.IO.Path.GetFileName(from.TrimEnd(System.IO.Path.DirectorySeparatorChar)));
            if (string.Equals(from, to, StringComparison.Ordinal)) return new FileValue(to);
            if (isFolder && to.StartsWith(from.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new KScriptException($"'{from}' can't be {(move ? "moved" : "copied")} into itself ('{to}'). Choose a folder outside it.", "Argument");
            if (!overwrite && (File.Exists(to) || Directory.Exists(to)))
                throw new KScriptException($"'{to}' already exists. Add overwrite: true to replace it.", "IO");
            EnsureFolder(to);

            if (!isFolder)
            {
                if (move) File.Move(from, to, overwrite);
                else File.Copy(from, to, overwrite);
            }
            else if (move)
            {
                if (overwrite && Directory.Exists(to)) Directory.Delete(to, recursive: true);
                Directory.Move(from, to);
            }
            else
            {
                CopyFolder(from, to, overwrite);
            }
            return new FileValue(to);
        }

        private static void CopyFolder(string from, string to, bool overwrite)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from)) File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)), overwrite);
            foreach (string dir in Directory.GetDirectories(from)) CopyFolder(dir, System.IO.Path.Combine(to, System.IO.Path.GetFileName(dir)), overwrite);
        }

        private static string KnownFolder(CallContext c, string name)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string Special(Environment.SpecialFolder folder, string xdg, string fallback) =>
                (OperatingSystem.IsLinux() ? XdgUserDir(home, xdg) : null)
                ?? (Environment.GetFolderPath(folder) is { Length: > 0 } d && d != home ? d : System.IO.Path.Combine(home, fallback));

            return name.Trim().ToLowerInvariant() switch
            {
                "home" => home,
                "desktop" => Special(Environment.SpecialFolder.DesktopDirectory, "DESKTOP", "Desktop"),
                "documents" => Special(Environment.SpecialFolder.MyDocuments, "DOCUMENTS", "Documents"),
                "downloads" => (OperatingSystem.IsLinux() ? XdgUserDir(home, "DOWNLOAD") : null)
                    ?? (OperatingSystem.IsWindows() ? WindowsDownloads() : null)
                    ?? System.IO.Path.Combine(home, "Downloads"),
                "music" => Special(Environment.SpecialFolder.MyMusic, "MUSIC", "Music"),
                "pictures" => Special(Environment.SpecialFolder.MyPictures, "PICTURES", "Pictures"),
                "videos" => Special(Environment.SpecialFolder.MyVideos, "VIDEOS", OperatingSystem.IsMacOS() ? "Movies" : "Videos"),
                "appdata" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                _ => System.IO.Path.GetTempPath(), // temp: the name's type allows only these names
            };
        }

        /// <summary>Reads XDG_&lt;key&gt;_DIR from the environment or ~/.config/user-dirs.dirs (Linux desktops rename these folders per language).</summary>
        private static string XdgUserDir(string home, string key)
        {
            string value = Environment.GetEnvironmentVariable("XDG_" + key + "_DIR");
            if (string.IsNullOrEmpty(value))
            {
                string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : System.IO.Path.Combine(home, ".config");
                string file = System.IO.Path.Combine(config, "user-dirs.dirs");
                if (!File.Exists(file)) return null;
                string prefix = "XDG_" + key + "_DIR=";
                value = File.ReadLines(file).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length).Trim('"');
            }
            if (string.IsNullOrEmpty(value)) return null;
            value = value.Replace("$HOME", home);
            // XDG uses $HOME itself for folders that are turned off; that's no answer for "downloads".
            return System.IO.Path.IsPathRooted(value) && System.IO.Path.GetFullPath(value).TrimEnd('/') != home.TrimEnd('/') ? value : null;
        }

        // Runs only on Windows, so a Linux coverage run can't reach it.
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static string WindowsDownloads()
        {
            var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(downloads, 0, IntPtr.Zero, out IntPtr path) != 0) return null;
            try
            {
                return System.Runtime.InteropServices.Marshal.PtrToStringUni(path);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(path);
            }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
    }
}
