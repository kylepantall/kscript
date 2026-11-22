using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KScript.Compile
{
    /// <summary>What a compiled program starts as: a script, a desktop window, or a web site.</summary>
    internal enum ProgramKind { Script, Window, Site }

    /// <summary>Everything that goes into one compiled program, whatever the target.</summary>
    internal sealed class ProgramPlan
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string Id { get; set; }
        public string Version { get; set; } = "1.0.0";
        public ProgramKind Kind { get; set; }
        /// <summary>The script (or, for a site, the site root) relative to the embedded files.</summary>
        public string Main { get; set; }
        /// <summary>The packages the program uses, KScript.Core included.</summary>
        public SortedSet<string> Packages { get; } = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The embedded files by path ("lib/rules.ks.xml"), with / separators.</summary>
        public SortedDictionary<string, byte[]> Files { get; } = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        public string IconPath { get; set; }
    }

    /// <summary>
    /// Turns a release kscript (a .NET single-file bundle) into a program: it drops the assemblies the program's
    /// packages don't need (the command line, the language server, unused packages and their libraries), adds the
    /// script and its files, and points the runtime config at them, so the program starts straight into the script.
    /// </summary>
    internal static class ProgramPackager
    {
        /// <summary>The runtimeconfig.json property that marks a compiled program: the path of its manifest among the
        /// files the .NET host extracts. KScriptApp's CompiledApp looks for it.</summary>
        public const string ConfigProperty = "KScript.CompiledApp";
        public const string Folder = "kscript-app";
        public const string ManifestName = "kscript-app.json";

        /// <summary>Packages that need ASP.NET Core, which is the second-largest part of kscript.</summary>
        private static readonly string[] NeedAspNet = { "KScript.Web", "KScript.Http" };

        public static SingleFileBundle Package(SingleFileBundle runtime, ProgramPlan plan, out List<string> dropped)
        {
            BundleEntry depsEntry = runtime.FindType(BundleFileType.DepsJson) ?? throw new CompileException("The kscript runtime has no deps.json.");
            BundleEntry configEntry = runtime.FindType(BundleFileType.RuntimeConfigJson) ?? throw new CompileException("The kscript runtime has no runtimeconfig.json.");
            string entryName = configEntry.RelativePath.Substring(0, configEntry.RelativePath.Length - ".runtimeconfig.json".Length);
            BundleEntry entryAssembly = runtime.Find(entryName + ".dll") ?? throw new CompileException($"The kscript runtime has no {entryName}.dll.");
            if (!SupportsPrograms(entryAssembly.Content()))
                throw new CompileException("This kscript runtime is from a release that can't run compiled programs. Use kscript 0.4.0 or later.");

            JsonObject deps = JsonNode.Parse(depsEntry.Content()).AsObject();
            var keep = KeptLibraries(deps, entryName, plan.Packages);
            var droppedFiles = TrimDeps(deps, keep, entryName);

            var bundle = new SingleFileBundle
            {
                Host = runtime.Host,
                MajorVersion = runtime.MajorVersion,
                MinorVersion = runtime.MinorVersion,
                Flags = runtime.Flags,
            };
            dropped = new List<string>();
            foreach (BundleEntry e in runtime.Entries)
            {
                if (e == depsEntry)
                    bundle.Entries.Add(BundleEntry.FromBytes(e.RelativePath, e.Type, Encoding.UTF8.GetBytes(deps.ToJsonString(new JsonSerializerOptions { WriteIndented = true }))));
                else if (e == configEntry)
                    bundle.Entries.Add(BundleEntry.FromBytes(e.RelativePath, e.Type, MarkConfig(e.Content())));
                else if (e.RelativePath.StartsWith(Folder + "/", StringComparison.Ordinal))
                    continue; // a runtime that was itself a program: never carry its files
                else if (droppedFiles.Contains(e.RelativePath))
                    dropped.Add(e.RelativePath);
                else
                    bundle.Entries.Add(e);
            }

            foreach (var (path, data) in plan.Files) bundle.Entries.Add(BundleEntry.FromBytes(Folder + "/app/" + path, BundleFileType.Unknown, data));
            bundle.Entries.Add(BundleEntry.FromBytes(Folder + "/" + ManifestName, BundleFileType.Unknown, Manifest(plan)));
            bundle.BundleId = bundle.ComputeId();
            return bundle;
        }

        /// <summary>The manifest the program reads when it starts.</summary>
        internal static byte[] Manifest(ProgramPlan plan) => Encoding.UTF8.GetBytes(new JsonObject
        {
            ["name"] = plan.Name,
            ["version"] = plan.Version,
            ["kind"] = plan.Kind.ToString().ToLowerInvariant(),
            ["main"] = "app/" + plan.Main,
            ["packages"] = new JsonArray(plan.Packages.Select(p => (JsonNode)p).ToArray()),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        /// <summary>True when the kscript assembly knows the compiled-program property (0.4.0 and later).</summary>
        internal static bool SupportsPrograms(byte[] assembly) =>
            SingleFileBundle.IndexOf(assembly, Encoding.Unicode.GetBytes(ConfigProperty), 0) >= 0;

        private static byte[] MarkConfig(byte[] json)
        {
            JsonObject config = JsonNode.Parse(json).AsObject();
            JsonObject options = config["runtimeOptions"] as JsonObject ?? new JsonObject();
            config["runtimeOptions"] = options;
            JsonObject properties = options["configProperties"] as JsonObject ?? new JsonObject();
            options["configProperties"] = properties;
            properties[ConfigProperty] = Folder + "/" + ManifestName;
            return Encoding.UTF8.GetBytes(config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>The libraries to keep: the framework, the kscript entry assembly, and everything the packages need.</summary>
        internal static HashSet<string> KeptLibraries(JsonObject deps, string entryName, IEnumerable<string> packages)
        {
            var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in deps["targets"].AsObject())
            {
                foreach (var library in target.Value.AsObject())
                {
                    string name = NameOf(library.Key);
                    if (!graph.TryGetValue(name, out var list)) graph[name] = list = new List<string>();
                    if (library.Value["dependencies"] is JsonObject d) list.AddRange(d.Select(p => p.Key));
                }
            }

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entryName };
            var queue = new Queue<string>(new[] { "KScript.Core" }.Concat(packages));
            while (queue.Count > 0)
            {
                string name = queue.Dequeue();
                if (!keep.Add(name) || !graph.TryGetValue(name, out var next)) continue;
                foreach (string n in next) queue.Enqueue(n);
            }

            // Packages named System.* and Microsoft.Extensions.* replace framework assemblies with newer versions
            // (System.Text.Json 10 for 8's, and the logging abstractions Npgsql brings for ASP.NET Core's), and every
            // kscript assembly was built against those, so they stay whatever the program imports.
            foreach (var library in deps["libraries"].AsObject())
            {
                string name = NameOf(library.Key);
                if (library.Value["type"]?.GetValue<string>() == "package" && ReplacesFramework(name) && keep.Add(name) && graph.TryGetValue(name, out var more))
                    foreach (string n in more) keep.Add(n);
            }

            bool aspnet = NeedAspNet.Any(keep.Contains);
            foreach (var library in deps["libraries"].AsObject())
            {
                if (library.Value["type"]?.GetValue<string>() != "runtimepack") continue;
                string name = NameOf(library.Key);
                if (aspnet || !name.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase)) keep.Add(name);
            }
            return keep;
        }

        private static bool ReplacesFramework(string name) =>
            name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal);

        /// <summary>Removes the dropped libraries from deps.json and returns the bundle paths of their files.</summary>
        internal static HashSet<string> TrimDeps(JsonObject deps, HashSet<string> keep, string entryName)
        {
            var droppedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in deps["targets"].AsObject())
            {
                JsonObject libraries = target.Value.AsObject();
                foreach (var library in libraries.ToList())
                {
                    string name = NameOf(library.Key);
                    var files = keep.Contains(name) ? keptFiles : droppedFiles;
                    foreach (string file in BundlePaths(library.Value as JsonObject)) files.Add(file);
                    if (!keep.Contains(name))
                    {
                        libraries.Remove(library.Key);
                    }
                    else if (library.Value["dependencies"] is JsonObject dependencies)
                    {
                        foreach (var d in dependencies.ToList().Where(d => !keep.Contains(d.Key))) dependencies.Remove(d.Key);
                    }
                }
            }
            foreach (var library in deps["libraries"].AsObject().ToList())
            {
                if (!keep.Contains(NameOf(library.Key))) deps["libraries"].AsObject().Remove(library.Key);
            }
            droppedFiles.ExceptWith(keptFiles);
            return droppedFiles;
        }

        /// <summary>Where a library's files sit in the bundle: published files are flat, satellite assemblies in a culture folder.</summary>
        private static IEnumerable<string> BundlePaths(JsonObject library)
        {
            if (library == null) yield break;
            foreach (string section in new[] { "runtime", "native" })
            {
                if (library[section] is JsonObject assets)
                    foreach (var a in assets) yield return Path.GetFileName(a.Key);
            }
            if (library["resources"] is JsonObject resources)
            {
                foreach (var r in resources)
                {
                    string culture = r.Value?["locale"]?.GetValue<string>();
                    yield return culture == null ? Path.GetFileName(r.Key) : culture + "/" + Path.GetFileName(r.Key);
                }
            }
        }

        private static string NameOf(string key)
        {
            int slash = key.IndexOf('/');
            return slash < 0 ? key : key.Substring(0, slash);
        }
    }
}
