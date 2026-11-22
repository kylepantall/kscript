using System.Text;

using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Storage
{
    /// <summary>
    /// One of an app's folders (config, data, cache, state or logs), with methods to read and write the files in it:
    /// <c>@storage(config)->write_json('settings.json', $settings)</c>. Names are paths inside the folder; a name that
    /// would leave it (an absolute path, or ..) is refused.
    /// </summary>
    public sealed class StorageFolder : IKObject
    {
        public StorageFolder(string app, string kind, string path)
        {
            App = app;
            Kind = kind;
            Path = System.IO.Path.GetFullPath(path);
        }

        public string App { get; }
        public string Kind { get; }
        public string Path { get; }

        public string TypeName => KType.Storage;

        public string ToText() => Path;

        public override string ToString() => Path;

        public override bool Equals(object obj) => obj is StorageFolder f && f.Path == Path;

        public override int GetHashCode() => Path.GetHashCode();

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "path" => Path,
                "app" => App,
                "kind" => Kind,
                _ => null,
            };
            return value != null;
        }

        /// <summary>The full path of a name inside the folder. Throws when the name would leave it.</summary>
        public string Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new KScriptException("The file name is empty. Give a name inside the folder, such as 'settings.json'.", "Argument");
            string relative = name.Trim().Replace('\\', '/');
            if (System.IO.Path.IsPathRooted(relative) || relative.Split('/').Contains(".."))
                throw new KScriptException($"'{name}' isn't inside the app's {Kind} folder. Use a name such as 'settings.json' or 'notes/today.txt'.", "Permission");
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relative));
        }

        /// <summary>Methods go through the package, so their arguments are bound by name first (default:, ttl:, recursive).</summary>
        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        /// <summary>Runs a method with its arguments already bound to their places.</summary>
        internal bool Invoke(CallContext call, string name, out object result)
        {
            result = name switch
            {
                "path_of" => Resolve(call.Str(0)),
                "exists" => call.Has(0) && call.Arg(0) != null ? Exists(Resolve(call.Str(0))) : Directory.Exists(Path),
                "read" => Read(call, File.ReadAllText),
                "read_json" => Read(call, path => Values.ParseJson(File.ReadAllText(path))),
                "write" => Write(call, call.Str(1), append: false),
                "append" => Write(call, call.Str(1), append: true),
                "write_json" => Write(call, Values.ToJson(call.Arg(1), indented: true), append: false),
                "delete" => Delete(call),
                "list" => List(call),
                "folder" => new StorageFolder(App, Kind, Resolve(call.Str(0))),
                "clear" => Clear(call),
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

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        /// <summary>A file's contents, or the default (null) when the file isn't there yet.</summary>
        private object Read(CallContext call, Func<string, object> read)
        {
            string path = Resolve(call.Str(0));
            return File.Exists(path) ? read(path) : call.Arg(1);
        }

        private object Write(CallContext call, string text, bool append)
        {
            call.Host.CheckWrite($"->{call.Name}()");
            string path = Resolve(call.Str(0));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            if (append)
            {
                File.AppendAllText(path, text, Encoding.UTF8);
                return null;
            }
            // Written beside the file and then moved over it, so a crash never leaves half a settings file.
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
            return null;
        }

        private object Delete(CallContext call)
        {
            call.Host.CheckWrite("->delete()");
            string path = Resolve(call.Str(0));
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            if (!Directory.Exists(path)) return false;
            Directory.Delete(path, recursive: true);
            return true;
        }

        private object List(CallContext call)
        {
            if (!Directory.Exists(Path)) return new List<object>();
            // A regex (/\.json$/) is matched against each name, folders included; text is a wildcard such as '*.json'.
            RegexValue regex = call.Arg(0) as RegexValue;
            string pattern = regex == null && call.Has(0) && call.Arg(0) != null ? call.Str(0) : "*";
            var option = call.Bool(1) ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(Path, pattern, option)
                .Select(f => System.IO.Path.GetRelativePath(Path, f).Replace('\\', '/'))
                .Where(f => regex == null || RegexValue.Run(regex, r => r.IsMatch(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Cast<object>()
                .ToList();
        }

        private object Clear(CallContext call)
        {
            call.Host.CheckWrite("->clear()");
            if (!Directory.Exists(Path)) return 0d;
            int count = 0;
            foreach (string file in Directory.GetFiles(Path))
            {
                File.Delete(file);
                count++;
            }
            foreach (string dir in Directory.GetDirectories(Path))
            {
                count += Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
                Directory.Delete(dir, recursive: true);
            }
            return (double)count;
        }

        internal static readonly (string Name, string Type, string Description)[] Fields =
        {
            ("path", KType.String, "The folder's full path."),
            ("app", KType.String, "The app the folder belongs to."),
            ("kind", KType.String, "Which of the app's folders it is: config, data, cache, state or logs."),
        };

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("path_of", "name", "The full path of a file in the folder. The folder's own path is the field .path.", KType.String),
            ("exists", "name?", "True if the file (or, without a name, the folder) is there.", KType.Bool),
            ("read", "name, default?", "A file's text, or default (null) when it isn't there yet.", KType.String),
            ("read_json", "name, default?", "A JSON file read into maps and lists, or default (null) when it isn't there yet.", KType.Any),
            ("write", "name, text", "Writes text to a file, replacing it safely. Folders are created as needed.", KType.Null),
            ("append", "name, text", "Adds text to the end of a file.", KType.Null),
            ("write_json", "name, value", "Writes a value to a file as indented JSON, replacing it safely.", KType.Null),
            ("delete", "name", "Deletes a file, or a folder and everything in it. True if something was deleted.", KType.Bool),
            ("list", "pattern?, recursive?: bool", "The names of the files in the folder, such as ['notes/a.txt', 'settings.json']. pattern is a wildcard like '*.json', or a regex like /^notes\\/.*\\.md$/ matched against the whole name.", KType.List),
            ("folder", "name", "A folder inside this one, with the same methods.", KType.Storage),
            ("clear", "", "Deletes everything in the folder. Returns how many files were deleted.", KType.Number),
        };
    }

    /// <summary>
    /// KScript.Storage: the folders an app keeps its files in, in the right place on each platform, named after the app
    /// (app.name in kscript.json, else the script's name).
    /// </summary>
    public sealed class StoragePackage : KScriptPackage
    {
        public override string Name => "KScript.Storage";

        public override string Description => "App folders for config, data, cache, state and logs, in the right place on each platform.";

        /// <summary>The app's name for a call: the app argument, else the script's project or name.</summary>
        public static string AppName(CallContext c, int appArgument)
        {
            if (c.Has(appArgument) && c.Arg(appArgument) != null) return AppFolders.CleanName(c.Str(appArgument));
            return AppFolders.CleanName(AppFolders.AppNameFor(c.Module?.Directory ?? c.Host.WorkingDirectory, c.Module?.Path));
        }

        /// <summary>One of the app's folders.</summary>
        public static StorageFolder Folder(CallContext c, int kindArgument, int appArgument)
        {
            string kind = AppFolders.CheckKind(c.Has(kindArgument) && c.Arg(kindArgument) != null ? c.Str(kindArgument) : "data");
            string app = AppName(c, appArgument);
            return new StorageFolder(app, kind, AppFolders.Folder(app, kind));
        }

        protected override void Register(PackageBuilder p)
        {
            foreach (var f in StorageFolder.Fields)
                p.Field(KType.Storage, f.Name, f.Type, f.Description);
            foreach (var m in StorageFolder.Methods)
                p.Method(KType.Storage, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((StorageFolder)t).Invoke(c, m.Name, out object r) ? r : null);

            p.Command("storage", "kind?: config|data|cache|state|logs, app?",
                "One of the app's folders, in the right place for this platform: config (settings), data (the app's files), cache (files that can be made again), state (history, recent files) or logs. The default is data. The app is app.name in kscript.json, else the script's name.",
                KType.Storage, c => Folder(c, 0, 1),
                "<do>@storage(config)->write_json('settings.json', { theme: 'dark' })</do>\n<echo>{ @storage(config)->read_json('settings.json').theme }</echo>");
            p.Command("app_name", "", "The name the app's folders are named after: app.name in kscript.json, else name, else the script's name.", KType.String, c => AppName(c, 99),
                "<echo>{ @app_name() }</echo>");
            p.Command("app_folders", "app?", "Every folder of the app, by kind: { config: ..., data: ..., cache: ..., state: ..., logs: ... }.", KType.Map, c =>
            {
                string app = AppName(c, 0);
                var map = new Dictionary<string, object>();
                foreach (string kind in AppFolders.Kinds) map[kind] = AppFolders.Folder(app, kind);
                return map;
            }, "<echo>{ @app_folders(app: 'Notes').cache }</echo>");

            p.Examples(
                ("storage->path_of", "<echo>{ @storage(data)->path_of('notes/today.txt') }</echo>"),
                ("storage->exists", "<echo>{ @storage(config)->exists('settings.json') }</echo>"),
                ("storage->read", "<echo>{ @storage(data)->read('notes.txt', default: '') }</echo>"),
                ("storage->read_json", "<let name=\"settings\" value=\"@storage(config)->read_json('settings.json', default: { theme: 'light' })\" />"),
                ("storage->write", "<do>@storage(data)->write('notes/today.txt', 'Buy milk')</do>"),
                ("storage->append", "<do>@storage(logs)->append('app.log', 'Started' + @char_of(10))</do>"),
                ("storage->write_json", "<do>@storage(config)->write_json('settings.json', { theme: 'dark', size: 14 })</do>"),
                ("storage->delete", "<do>@storage(cache)->delete('thumbnails')</do>"),
                ("storage->list", "<for each=\"name\" in=\"@storage(data)->list('*.txt', recursive)\"><echo>{ $name }</echo></for>"),
                ("storage->folder", "<let name=\"notes\" value=\"@storage(data)->folder('notes')\" />\n<do>$notes->write('today.txt', 'Buy milk')</do>"),
                ("storage->clear", "<echo>{ @storage(cache)->clear() } files removed</echo>"));
        }
    }
}
