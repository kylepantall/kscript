using System.Text;

using KScript.Metadata;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.IO
{
    /// <summary>A file or folder path with methods: <c>$file->name()</c>, <c>->extension()</c>, <c>->size()</c>...</summary>
    public sealed class FileValue : IKObject
    {
        public FileValue(string path) => Path = System.IO.Path.GetFullPath(path);

        public string Path { get; }

        public string TypeName => KType.File;

        public string ToText() => Path;

        public override string ToString() => Path;

        public override bool Equals(object obj) => obj is FileValue f && f.Path == Path;

        public override int GetHashCode() => Path.GetHashCode();

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "path" => Path,
                "name" => System.IO.Path.GetFileName(Path),
                "extension" => Extension(),
                _ => null,
            };
            return value != null;
        }

        private string Extension() => System.IO.Path.GetExtension(Path).TrimStart('.');

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            call.Host.CheckPath(Path);
            result = name switch
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
                "read_text" => File.ReadAllText(Path),
                "read_lines" => File.ReadAllLines(Path).Cast<object>().ToList(),
                "join" => Checked(call, new FileValue(System.IO.Path.Combine(new[] { Path }.Concat(call.Args.Select(Values.ToStr)).ToArray()))),
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

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("path", "", "The full path as text.", KType.String),
            ("name", "", "The file name with its extension: report.pdf.", KType.String),
            ("stem", "", "The file name without its extension: report.", KType.String),
            ("extension", "", "The extension without the dot: pdf (empty if there isn't one).", KType.String),
            ("directory", "", "The folder the file is in.", KType.File),
            ("exists", "", "True if a file or folder is there.", KType.Bool),
            ("is_file", "", "True if it is a file.", KType.Bool),
            ("is_folder", "", "True if it is a folder.", KType.Bool),
            ("size", "", "The size in bytes (0 for folders).", KType.Number),
            ("modified", "", "When the file was last changed.", KType.Date),
            ("created", "", "When the file was created.", KType.Date),
            ("read_text", "", "The file's text.", KType.String),
            ("read_lines", "", "The file's lines.", KType.List),
            ("join", "parts...", "A path inside this folder: $dir->join('a', 'b.txt').", KType.File),
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

        private static string PathOf(ElementRun run, string attribute) => Resolve(run.Eval(attribute), run.Interpreter.Host, run.Module.Directory);

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
            foreach (var m in FileValue.Methods)
                p.Method(KType.File, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((FileValue)t).TryCallMethod(c, m.Name, out object r) ? r : null);

            p.Command("file", "path", "A file value for a path, with methods like ->name(), ->extension() and ->size().", KType.File, c => new FileValue(PathOf(c.Need(0), c)),
                "<let name=\"report\" value=\"@file('out/report.pdf')\" />");
            p.Command("read_text", "path", "The text of a file. Relative paths are from the script's folder.", KType.String, c => File.ReadAllText(PathOf(c.Need(0), c)),
                "<let name=\"config\" value=\"@read_text('config.json') |> @json()\" />");
            p.Command("read_lines", "path", "The lines of a file.", KType.List, c => File.ReadAllLines(PathOf(c.Need(0), c)).Cast<object>().ToList());
            p.Command("read_json", "path, as?: type", "A JSON file read into maps and lists. With as: a <type>, the data is checked and turned into records.", KType.Any, c => Shapes.MapJson(c, Values.ParseJson(File.ReadAllText(PathOf(c.Need(0), c))), 1));
            p.Command("write_text", "path, text", "Writes text to a file, replacing it. Folders are created as needed.", KType.Null, c => Write(c, c.Str(1), append: false));
            p.Command("append_text", "path, text", "Adds text to the end of a file.", KType.Null, c => Write(c, c.Str(1), append: true));
            p.Command("write_json", "path, value", "Writes a value to a file as indented JSON.", KType.Null, c => Write(c, Values.ToJson(c.Arg(1), indented: true), append: false));
            p.Command("exists", "path", "True if a file or folder is at the path.", KType.Bool, c =>
            {
                string path = PathOf(c.Need(0), c);
                return File.Exists(path) || Directory.Exists(path);
            });
            p.Command("is_file", "path", "True if a file is at the path.", KType.Bool, c => File.Exists(PathOf(c.Need(0), c)));
            p.Command("is_folder", "path", "True if a folder is at the path.", KType.Bool, c => Directory.Exists(PathOf(c.Need(0), c)));
            p.Command("files", "folder, pattern?, recursive?: bool", "The files in a folder, as file values. pattern is a wildcard like '*.txt'.", KType.List,
                c => List(c, Directory.EnumerateFiles), "<for each=\"f\" in=\"@files('.', '*.log')\"><echo>{$f->name()}</echo></for>");
            p.Command("folders", "folder, pattern?, recursive?: bool", "The folders in a folder, as file values.", KType.List, c => List(c, Directory.EnumerateDirectories));
            p.Command("join_path", "base, parts...", "Joins a folder and the parts below it with the right separator. Parts may be a list: @join_path(base: $folder, ['archive', $name]).", KType.String, c =>
            {
                static IEnumerable<object> Flatten(object a) => a is List<object> list ? list.SelectMany(Flatten) : new[] { a };
                return System.IO.Path.Combine(c.Args.SelectMany(Flatten).Select(a => a is FileValue f ? f.Path : Values.ToStr(a)).ToArray());
            });
            p.Command("full_path", "path", "The absolute path.", KType.String, c => PathOf(c.Need(0), c));
            p.Command("cwd", "", "The folder kscript was run from.", KType.String, c => c.Host.WorkingDirectory);
            p.Command("home", "", "The user's home folder.", KType.String, c => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            p.Command("temp_folder", "", "The folder for temporary files.", KType.String, c => System.IO.Path.GetTempPath());
            p.Command("known_folder", "name: home|desktop|documents|downloads|music|pictures|videos|appdata|temp", "A standard folder: home, desktop, documents, downloads, music, pictures, videos, appdata or temp.", KType.String, c => KnownFolder(c, c.Str(0)));

            p.Element(new ElementInfo("write", "Writes the element text (or value) to a file. Folders are created as needed.", ContentKind.Template,
                new A("to", K.Expression, "The file to write.", true),
                new A("value", K.Expression, "Write this value instead of the text (maps and lists are written as JSON)."),
                new A("append", K.Bool, "yes adds to the end instead of replacing the file."))
            {
                Context = ElementContext.Script,
                Example = "<write to=\"'log.txt'\" append=\"yes\">{ @now() }: done</write>",
            }, run =>
            {
                run.Interpreter.Host.CheckWrite("<write>");
                string path = PathOf(run, "to");
                object value = run.Has("value") ? run.Eval("value") : null;
                string text = run.Has("value") ? value is Dictionary<string, object> or List<object> ? Values.ToJson(value, indented: true) : Values.ToStr(value) : run.Text();
                EnsureFolder(path);
                if (run.Flag("append")) AppendLine(path, text);
                else File.WriteAllText(path, text);
                return null;
            });

            p.Element(new ElementInfo("move", "Moves (or renames) a file or folder.", ContentKind.Empty,
                new A("from", K.Expression, "The file or folder to move.", true),
                new A("to", K.Expression, "Where it goes: a full path, or an existing folder to move it into.", true),
                new A("overwrite", K.Bool, "yes replaces a file that is already there."))
            {
                Context = ElementContext.Script,
                Example = "<move from=\"$file\" to=\"@join_path($new_dir, $file->name())\" />",
            }, run => Transfer(run, move: true));

            p.Element(new ElementInfo("copy", "Copies a file or folder.", ContentKind.Empty,
                new A("from", K.Expression, "The file or folder to copy.", true),
                new A("to", K.Expression, "Where the copy goes: a full path, or an existing folder to copy it into.", true),
                new A("overwrite", K.Bool, "yes replaces files that are already there."))
            {
                Context = ElementContext.Script,
            }, run => Transfer(run, move: false));

            p.Element(new ElementInfo("delete", "Deletes a file, or a folder and everything in it. Does nothing if it isn't there.", ContentKind.Empty,
                new A("path", K.Expression, "The file or folder to delete.", true))
            {
                Context = ElementContext.Script,
            }, run =>
            {
                run.Interpreter.Host.CheckWrite("<delete>");
                string path = PathOf(run, "path");
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return null;
            });

            p.Element(new ElementInfo("mkdir", "Creates a folder (and any folders above it).", ContentKind.Empty,
                new A("path", K.Expression, "The folder to create.", true))
            {
                Context = ElementContext.Script,
            }, run =>
            {
                run.Interpreter.Host.CheckWrite("<mkdir>");
                Directory.CreateDirectory(PathOf(run, "path"));
                return null;
            });
        }

        private static object Write(CallContext c, string text, bool append)
        {
            c.Host.CheckWrite($"@{c.Name}()");
            string path = PathOf(c.Need(0), c);
            EnsureFolder(path);
            if (append) File.AppendAllText(path, text, Encoding.UTF8);
            else File.WriteAllText(path, text);
            return null;
        }

        /// <summary>Adds text to a file as a line of its own: after a line break, and ending with one.</summary>
        private static void AppendLine(string path, string text)
        {
            string before = string.Empty;
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                using FileStream f = File.OpenRead(path);
                f.Seek(-1, SeekOrigin.End);
                if (f.ReadByte() != '\n') before = Environment.NewLine;
            }
            File.AppendAllText(path, before + text + (text.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : Environment.NewLine));
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

        private static bool? Transfer(ElementRun run, bool move)
        {
            run.Interpreter.Host.CheckWrite(move ? "<move>" : "<copy>");
            string from = PathOf(run, "from");
            string to = PathOf(run, "to");
            bool overwrite = run.Flag("overwrite");
            bool isFolder = Directory.Exists(from);
            if (!isFolder && !File.Exists(from)) throw run.Error($"There is no file or folder at '{from}'.", "from", "FileNotFound");
            if (Directory.Exists(to)) to = System.IO.Path.Combine(to, System.IO.Path.GetFileName(from.TrimEnd(System.IO.Path.DirectorySeparatorChar)));
            if (string.Equals(from, to, StringComparison.Ordinal)) return null;
            if (!overwrite && (File.Exists(to) || Directory.Exists(to)))
                throw run.Error($"'{to}' already exists. Add overwrite=\"yes\" to replace it.", "to", "IO");
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
            return null;
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
                "temp" => System.IO.Path.GetTempPath(),
                _ => throw c.Error($"@known_folder() doesn't know '{name}'. Use home, desktop, documents, downloads, music, pictures, videos, appdata or temp.", "Argument"),
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
