using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using KScript.Projects;
using KScript.Runtime;
using KScript.Syntax.Markup;
using static KScript.Projects.LibraryDependency;

namespace KScript.Cli
{
    /// <summary>Installs a GitHub library as ordinary source files imported by their project-relative paths.</summary>
    public static partial class LibCommand
    {
        private const string Receipt = ".kscript-lib.json";
        private const long SizeLimit = 64L * 1024 * 1024;
        private const string Usage = "Usage: kscript lib add <library | owner/repo@library | owner/repo | GitHub URL> or kscript lib restore";

        public static int Run(string[] args, TextWriter output, TextWriter error) =>
            Run(args, output, error, WorkingFolder.Current, new HttpClientHandler { AllowAutoRedirect = false });

        internal static int Run(string[] args, TextWriter output, TextWriter error, string workingDirectory, HttpMessageHandler handler)
        {
            using var client = new HttpClient(handler) { MaxResponseContentBufferSize = SizeLimit };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("kscript-lib/1");
            if (args is ["add" or "restore", "--help" or "-h"])
            {
                CliReference.PrintCommand(output, CliReference.Find("lib"));
                return ExitCodes.Ok;
            }
            bool restore = args is ["restore"];
            Source source = null;
            if (!restore && (args.Length != 2 || args[0] != "add" || !Source.TryParse(args[1], out source)))
            {
                error.WriteLine(Usage);
                return ExitCodes.Usage;
            }
            try
            {
                ProjectConfig project = ProjectConfig.FindAndLoad(workingDirectory);
                if (restore && project == null)
                {
                    error.WriteLine($"No {ProjectConfig.FileName} was found. Run kscript lib restore in your project.");
                    return ExitCodes.Usage;
                }
                project ??= ProjectConfig.Default(workingDirectory);
                string libraryFolder = Path.GetFullPath(Path.Combine(project.SiteRoot, project.PathFor("lib")));
                CheckDestination(project.Directory, libraryFolder);
                if (restore)
                {
                    var pins = project.Libraries.Values.Select(d => (Source: Source.FromDependency(d), d.Commit)).ToArray();
                    foreach (var pin in pins)
                    {
                        source = pin.Source;
                        Install(client, source, pin.Commit, libraryFolder, project.SiteRoot, output,
                            requested: source.Url, requireCommit: true).GetAwaiter().GetResult();
                    }
                    if (project.Libraries.Count == 0) output.WriteLine("No project libraries to restore.");
                }
                else
                {
                    bool bareName = source.Named && args[1] == source.Name;
                    var installed = Add(client, source, bareName, project, libraryFolder, output).GetAwaiter().GetResult();
                    ProjectLibraries.Record(project.Directory, installed);
                }
                return ExitCodes.Ok;
            }
            catch (ProjectConfigException ex) { error.WriteLine(ex.Message); return ExitCodes.Usage; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or JsonException)
            {
                error.WriteLine($"Could not {(restore ? "restore" : "add")} {source?.Name ?? "project libraries"}: {ex.Message}");
                return ExitCodes.Failed;
            }
        }

        internal static bool TryParseRepository(string input, out string repository)
        {
            repository = null;
            if (!Source.TryParseDirect(input, out var source) || source.Ref != null) return false;
            repository = source.Repository;
            return true;
        }

        private sealed record Source(string Owner, string Repo, string Ref, string Folder)
        {
            internal string Repository => Owner + "/" + Repo;
            internal bool Named => Ref == null && Folder.Length > 0;
            internal bool Ambiguous => Folder.Length > 0 && Ref != null && !Ref.Contains('/');
            internal bool CatalogEntry => Folder.StartsWith("libraries/", StringComparison.Ordinal) && Folder.LastIndexOf('/') == "libraries".Length;
            internal string RootPackageName { get; init; }
            internal string Name => RootPackageName ?? (Folder.Length == 0 ? Repo : Folder.Split('/')[^1]);
            // A GitHub slug can be ".github" or "con". It can publish a library with a portable local name.
            internal bool CanInstall => Folder.Length > 0 || RootPackageName != null || IsLibraryName(Repo);
            internal string Url
            {
                get
                {
                    string url = $"https://github.com/{Repository}";
                    if (Ref == null && !Named) return url;
                    string tree = url + "/tree/" + Uri.EscapeDataString(Ref ?? "HEAD");
                    return Folder.Length == 0 ? tree : tree + "/" + string.Join('/', Folder.Split('/').Select(Uri.EscapeDataString));
                }
            }

            // GitHub's ordinary tree URLs leave slashes in a branch name unescaped.
            internal Source LongerRef()
            {
                int slash = Folder.IndexOf('/');
                return this with { Ref = Ref + "/" + (slash < 0 ? Folder : Folder[..slash]), Folder = slash < 0 ? "" : Folder[(slash + 1)..] };
            }

            // An unescaped slash may be part of a branch. A saved canonical URL settles the ambiguity offline.
            internal IEnumerable<Source> Interpretations()
            {
                yield return this;
                if (!Ambiguous) yield break;
                var candidate = this;
                while (candidate.Folder.Length > 0)
                {
                    candidate = candidate.LongerRef();
                    if (candidate.CanInstall) yield return candidate;
                }
            }

            internal static Source FromDependency(LibraryDependency dependency)
            {
                if (IsCommit(dependency.Commit) && TryParseDirect(dependency.Source, out var parsed) && parsed.Url == dependency.Source)
                {
                    if (parsed.Name == dependency.Name && parsed.CanInstall) return parsed;
                    if (parsed.IsRoot && IsLibraryName(dependency.Name)) return parsed with { RootPackageName = dependency.Name };
                }
                throw new ProjectConfigException($"Invalid dependency '{dependency.Name}' in kscript.json. Use its canonical GitHub source URL, matching library name, and full commit SHA.");
            }

            internal bool IsRoot => Ref == null && Folder.Length == 0;

            // Match only identities a fresh bare lookup can produce. Explicit branches, arbitrary folders and
            // other owners stay distinct even when a folder happens to have the requested name.
            internal static Source Published(string url, string name, Func<string, bool> eligible)
            {
                if (!TryParseDirect(url, out var parsed) || parsed.Url != url || !eligible(parsed.Repository)) return null;
                if (parsed.IsRoot) return parsed with { RootPackageName = name };
                return parsed.Ref == "HEAD" && parsed.Folder == "libraries/" + name ? parsed : null;
            }

            internal static bool TryParse(string input, out Source source)
            {
                if (TryParseDirect(input, out source)) return source.CanInstall;
                if (string.IsNullOrEmpty(input) || input.Contains("://")) return false;
                int at = input.IndexOf('@');
                string name = at < 0 ? input : input[(at + 1)..];
                if (!IsLibraryName(name)) return false;
                string repository = at < 0 ? LibraryRepositories.DefaultRepository : input[..at];
                if (repository.EndsWith('/') || !TryParseDirect(repository, out var root) || root.Ref != null) return false;
                source = root with { Folder = "libraries/" + name };
                return true;
            }

            internal static bool TryParseDirect(string input, out Source source)
            {
                source = null;
                const string host = "https://github.com/";
                if (string.IsNullOrEmpty(input) || input.Contains('#') || input.Contains('?')) return false;
                bool url = input.StartsWith(host, StringComparison.OrdinalIgnoreCase);
                string[] parts = (url ? input.Substring(host.Length) : input).TrimEnd('/').Split('/');
                if (parts.Length < 2 || (parts.Length != 2 && (!url || parts.Length < 4 || parts[2] != "tree"))) return false;
                string owner = parts[0].ToLowerInvariant(), repo = parts[1].ToLowerInvariant();
                if (repo.EndsWith(".git", StringComparison.Ordinal)) repo = repo[..^4];
                if (!Regex.IsMatch(owner, @"\A[a-z0-9][a-z0-9-]*\z") || !IsRepositorySlug(repo)) return false;
                string reference = parts.Length > 2 ? Uri.UnescapeDataString(parts[3]) : null;
                if (reference != null && !reference.Split('/').All(IsSafePathComponent)) return false;
                string[] folders = parts.Skip(4).Select(Uri.UnescapeDataString).ToArray();
                if (!folders.All(IsSafePathComponent) || folders.LastOrDefault()?.StartsWith('.') == true) return false;
                source = new Source(owner, repo, reference, string.Join('/', folders));
                return true;
            }
        }

        private static bool IsLibraryName(string name) => Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z") && IsSafePathComponent(name);

        private static bool IsRepositorySlug(string name) => Regex.IsMatch(name, @"\A[a-zA-Z0-9._-]+\z")
            && !name.EndsWith('.') && !name.Equals(".git", StringComparison.OrdinalIgnoreCase);

        internal static void CheckDestination(string root, string destination)
        {
            if (Path.GetRelativePath(root, destination) == ".") return;
            string path = root;
            foreach (string part in Path.GetRelativePath(root, destination).Split(Path.DirectorySeparatorChar))
            {
                if (!IsSafePathComponent(part)) throw new IOException("Choose a library folder inside the project.");
                path = Path.Combine(path, part);
                if (new DirectoryInfo(path).LinkTarget != null) throw new IOException($"The library path includes a symbolic link: {path}");
            }
        }

        private static async Task<LibraryDependency> Add(HttpClient client, Source source, bool bareName, ProjectConfig project, string libraryFolder, TextWriter output)
        {
            var candidates = source.Interpretations().ToArray();
            foreach (var candidate in candidates)
            {
                if (!project.Libraries.TryGetValue(candidate.Name, out var dependency)) continue;
                var pinned = Source.FromDependency(dependency);
                if (bareName || pinned.Url == candidate.Url || source.Named && pinned.IsRoot && pinned.Repository == source.Repository
                    || await RootReferencesPin(client, candidate, pinned, dependency.Commit))
                {
                    if (!bareName && source.Named && pinned.IsRoot) pinned = pinned with { RootPackageName = candidate.Name };
                    return await Install(client, pinned, dependency.Commit, libraryFolder, project.SiteRoot,
                        output, pinned.Url, requireCommit: true);
                }
            }
            if (source.Named && !LibraryRepositories.Contains(project.Directory, source.Repository))
                throw new ProjectConfigException($"Library repository {source.Repository} is not registered for this project. Run kscript repo add {source.Repository} first.");

            if (candidates.All(c => project.Libraries.ContainsKey(c.Name))) throw Conflict(source.Name);
            foreach (var possibilities in candidates.GroupBy(c => c.Name))
            {
                if (project.Libraries.ContainsKey(possibilities.Key)) continue;
                string destination = Path.Combine(libraryFolder, possibilities.Key);
                if (source.Ambiguous && new DirectoryInfo(destination).LinkTarget != null) continue;
                CheckDestination(libraryFolder, destination);
                var existing = Existing(possibilities, null, destination, project.SiteRoot, output, probe: source.Ambiguous,
                    published: source.Named ? url => Source.Published(url, source.Name,
                        repo => bareName ? LibraryRepositories.Contains(project.Directory, repo) : repo == source.Repository) : null);
                if (existing != null) return existing;
            }
            if (bareName)
            {
                var published = await FindPublisherLibrary(client, source.Name, project.Directory);
                return await Install(client, published.Source, published.Commit, libraryFolder, project.SiteRoot, output, published.Source.Url, requireCommit: true);
            }
            return await Download(client, source, project, libraryFolder, output);
        }

        private static async Task<LibraryDependency> Download(HttpClient client, Source source, ProjectConfig project, string libraryFolder, TextWriter output)
        {
            string requested = source.Url;
            bool ambiguous = source.Ambiguous;
            string commit;
            while (true)
            {
                try
                {
                    commit = await ResolveCommit(client, source);
                    break;
                }
                // GitHub also uses 422 for an unknown commit/ref in an existing repo.
                catch (HttpRequestException ex) when (ambiguous && source.Folder.Length > 0 &&
                    ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
                {
                    source = source.LongerRef();
                    if (!source.CanInstall)
                        throw new IOException($"'{source.Repo}' cannot be used as a local library directory. Choose a named library or a folder within the repository.");
                }
                catch (HttpRequestException ex) when (source.Named && ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new IOException($"Could not find the public GitHub repository {source.Repository}.", ex);
                }
            }
            if (project.Libraries.ContainsKey(source.Name)) throw Conflict(source.Name);
            try { return await Install(client, source, commit, libraryFolder, project.SiteRoot, output, requested, requireCommit: false); }
            catch (MissingCatalogEntry) when (source.Named)
            {
                var root = source with { Folder = "", RootPackageName = source.Name };
                if (!await PublisherRootMatches(client, root, source.Name, commit)) throw;
                return await Install(client, root, commit, libraryFolder, project.SiteRoot, output, root.Url, requireCommit: true);
            }
        }

        private static async Task<string> ResolveCommit(HttpClient client, Source source)
        {
            string url = $"https://api.github.com/repos/{source.Repository}/commits/{Uri.EscapeDataString(source.Ref ?? "HEAD")}";
            using var metadata = JsonDocument.Parse(await client.GetByteArrayAsync(url));
            if (metadata.RootElement.ValueKind != JsonValueKind.Object || !metadata.RootElement.TryGetProperty("sha", out var sha)
                || sha.ValueKind != JsonValueKind.String || !IsCommit(sha.GetString()))
                throw new InvalidDataException("GitHub did not return a commit SHA.");
            return sha.GetString();
        }

        private static IOException Conflict(string name) =>
            new IOException($"A dependency named '{name}' already exists in kscript.json with a different source.");

        private static async Task<LibraryDependency> Install(HttpClient client, Source source, string commit, string libraryFolder,
            string site, TextWriter output, string requested, bool requireCommit)
        {
            string destination = Path.Combine(libraryFolder, source.Name);
            CheckDestination(libraryFolder, destination);
            var existing = Existing(new[] { source }, requireCommit ? commit : null, destination, site, output);
            if (existing != null) return existing;
            var submodule = source.CatalogEntry ? await ResolveSubmodule(client, source, commit) : null;
            string archiveRepo = submodule?.Repository ?? source.Repository;
            string archiveCommit = submodule?.Commit ?? commit;
            byte[] bytes = await client.GetByteArrayAsync($"https://codeload.github.com/{archiveRepo}/zip/{archiveCommit}");
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            // An upstream catalog can host several libraries or SDKs in other languages.
            string folder = source.Folder;
            if (submodule != null && !archive.Entries.Any(e => ArchivePath(e).StartsWith(folder + "/", StringComparison.Ordinal)))
                folder = "";
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            string stage = Path.Combine(parent, ".kscript-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                LibraryManifest manifest = ArchiveManifest(archive, folder, out string packageFolder);
                if (packageFolder != null)
                {
                    folder = folder.Length == 0 ? packageFolder : folder + "/" + packageFolder;
                    manifest = ArchiveManifest(archive, folder, out _);
                    if (manifest == null)
                        throw new InvalidDataException($"{folder}/{ProjectConfig.FileName} must declare the package entry and version.");
                }
                var scripts = Extract(archive, folder, archiveRepo, stage);
                if (source.RootPackageName != null && manifest?.Name != source.RootPackageName)
                    throw new InvalidDataException($"The manifest in {source.Repository} must declare the library name '{source.RootPackageName}'.");
                string entry = EntryFor(stage, scripts, manifest);
                var receipt = new JsonObject { ["source"] = source.Url, ["commit"] = commit, ["entry"] = entry };
                if (manifest != null)
                {
                    receipt["name"] = manifest.Name;
                    receipt["version"] = manifest.Version;
                    receipt["packages"] = new JsonArray(manifest.Packages.Select(p => (JsonNode)p).ToArray());
                }
                if (submodule != null) receipt["submodule"] = new JsonObject { ["repository"] = archiveRepo, ["commit"] = archiveCommit };
                if (requested != source.Url) receipt["requested"] = requested;
                File.WriteAllText(Path.Combine(stage, Receipt), receipt.ToJsonString() + "\n");
                try { Directory.Move(stage, destination); }
                catch (IOException) when (Directory.Exists(destination))
                {
                    existing = Existing(new[] { source }, requireCommit ? commit : null, destination, site, output);
                    if (existing != null) return existing;
                    throw;
                }
                Report(output, "Installed", archiveCommit, site, destination, entry, manifest?.Version);
                return new LibraryDependency(source.Name, source.Url, commit);
            }
            catch (ProjectConfigException ex) { throw new InvalidDataException($"Invalid library manifest: {ex.Message}", ex); }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
        }

        private static bool IsCommit(string value) => value != null && Regex.IsMatch(value, @"\A[0-9a-f]{40}\z");

        private sealed record Submodule(string Repository, string Commit);

        // Only this contents 404 permits falling back from the catalog entry to a manifest-declared root package.
        private sealed class MissingCatalogEntry(Source source, Exception cause)
            : IOException($"Folder {source.Folder}/ was not found in {source.Repository}.", cause) { }

        private static async Task<Submodule> ResolveSubmodule(HttpClient client, Source source, string commit)
        {
            string path = "libraries/" + Uri.EscapeDataString(source.Name);
            byte[] contents;
            try { contents = await client.GetByteArrayAsync($"https://api.github.com/repos/{source.Repository}/contents/{path}?ref={commit}"); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new MissingCatalogEntry(source, ex);
            }
            using var doc = JsonDocument.Parse(contents);
            var item = doc.RootElement;
            if (item.ValueKind == JsonValueKind.Array) return null; // A source folder in the catalog itself.
            string Field(string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var field)
                && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
            string sha = Field("sha"), url = Field("submodule_git_url");
            if (Field("type") is not ("submodule" or "file") || !IsCommit(sha) || string.IsNullOrEmpty(url))
                throw new InvalidDataException($"{source.Folder} must be a source directory or a Git submodule pinned to a commit.");

            // Git records remote URLs in multiple forms. Downloads always use codeload.github.com.
            const string scp = "git@github.com:", ssh = "ssh://git@github.com/";
            if (url.StartsWith(scp, StringComparison.OrdinalIgnoreCase)) url = "https://github.com/" + url[scp.Length..];
            else if (url.StartsWith(ssh, StringComparison.OrdinalIgnoreCase)) url = "https://github.com/" + url[ssh.Length..];
            else if (url.StartsWith("../", StringComparison.Ordinal) && !url.Contains('\\') &&
                     Uri.TryCreate(new Uri($"https://github.com/{source.Repository}/"), url, out var resolved))
                url = resolved.AbsoluteUri;
            if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) || !TryParseRepository(url, out string repo))
                throw new InvalidDataException($"The submodule at {source.Folder} must point to a public GitHub repository.");
            return new Submodule(repo, sha);
        }

        private static LibraryDependency Existing(IEnumerable<Source> candidates, string expectedCommit, string destination, string site, TextWriter output, bool probe = false,
            Func<string, Source> published = null)
        {
            string receipt = Path.Combine(destination, Receipt);
            if (!Directory.Exists(destination) && !File.Exists(destination)) return null;
            if (File.Exists(receipt) && new FileInfo(receipt).LinkTarget == null)
            {
                JsonObject saved;
                try { saved = JsonNode.Parse(File.ReadAllText(receipt)) as JsonObject; }
                catch (JsonException) when (probe) { return null; }
                string entry = saved?["entry"]?.ToString(), commit = saved?["commit"]?.ToString();
                string savedUrl = saved?["source"]?.ToString();
                var source = candidates.FirstOrDefault(c => c.Url == savedUrl) ?? published?.Invoke(savedUrl);
                if (source?.RootPackageName != null && saved?["name"]?.ToString() != source.RootPackageName) source = null;
                if (source != null && IsCommit(commit) && entry != null && entry.Split('/').All(IsSafePathComponent))
                {
                    if (expectedCommit != null && expectedCommit != commit)
                        throw new IOException($"{destination} is installed at {commit}, but kscript.json pins {expectedCommit}. Move the installed folder before restoring.");
                    CheckDestination(destination, Path.Combine(destination, entry));
                    if (!File.Exists(Path.Combine(destination, entry))) throw new IOException($"The installed library is missing {entry}.");
                    string pinned = (saved["submodule"] as JsonObject)?["commit"]?.ToString();
                    Report(output, "Already installed", IsCommit(pinned) ? pinned : commit, site, destination, entry, saved["version"]?.ToString());
                    return new LibraryDependency(source.Name, source.Url, commit);
                }
            }
            if (probe) return null;
            throw new IOException($"{destination} already exists. Move it before installing this library.");
        }

        private static void Report(TextWriter output, string action, string commit, string site, string destination, string entry, string version)
        {
            string name = Path.GetFileName(destination);
            if (!string.IsNullOrEmpty(version) && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))
                name += " " + version;
            output.WriteLine($"{action} {name} at {commit}.");
            output.WriteLine($"From a script in {site}: import \"{Path.GetRelativePath(site, Path.Combine(destination, entry)).Replace('\\', '/')}\"");
        }

        private static string ArchivePath(ZipArchiveEntry file)
        {
            int slash = file.FullName.IndexOf('/');
            if (slash < 1 || !IsRepositorySlug(file.FullName[..slash])) throw new InvalidDataException("Invalid GitHub archive root.");
            return file.FullName[(slash + 1)..];
        }

        private static LibraryManifest ArchiveManifest(ZipArchive archive, string folder, out string packageFolder)
        {
            packageFolder = null;
            string path = folder.Length == 0 ? ProjectConfig.FileName : folder + "/" + ProjectConfig.FileName;
            ZipArchiveEntry config = null;
            foreach (var file in archive.Entries)
            {
                if (ArchivePath(file) != path) continue;
                if (config != null) throw new InvalidDataException($"Duplicate library manifest: {path}");
                CheckArchiveEntry(file, path);
                config = file;
            }
            if (config == null) return null;
            if (config.Length > SizeLimit) throw new InvalidDataException("The library exceeds 64 MiB.");
            using var content = new MemoryStream();
            CopyEntry(config, content, new byte[81920]);
            content.Position = 0;
            using var reader = new StreamReader(content);
            return LibraryManifest.Parse(reader.ReadToEnd(), path, out packageFolder);
        }

        private static void CheckArchiveEntry(ZipArchiveEntry file, string relative)
        {
            int kind = (file.ExternalAttributes >> 16) & 0xf000;
            if ((file.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || (kind != 0 && kind != 0x8000 && kind != 0x4000))
                throw new InvalidDataException($"Unsupported link or special file: {relative}");
        }

        private static void CopyEntry(ZipArchiveEntry file, Stream output, byte[] buffer)
        {
            using var input = file.Open();
            long remaining = file.Length;
            while (remaining > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                if (read == 0) throw new InvalidDataException($"Incomplete library file: {file.FullName}");
                output.Write(buffer, 0, read);
                remaining -= read;
            }
            if (input.ReadByte() != -1) throw new InvalidDataException($"Incorrect size for library file: {file.FullName}");
        }

        private static List<string> Extract(ZipArchive archive, string folder, string repository, string stage)
        {
            var scripts = new List<string>();
            string prefix = folder.Length == 0 ? "" : folder + "/";
            byte[] buffer = new byte[81920];
            long total = 0;
            bool foundFolder = false;
            foreach (var file in archive.Entries)
            {
                // GitHub prefixes every archive path with a generated root directory.
                string path = ArchivePath(file);
                if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                foundFolder = true;
                string relative = path[prefix.Length..].TrimEnd('/');
                if (relative.Length == 0) continue;
                if (!relative.Split('/').All(IsSafePathComponent) || relative.Split('/')[0].Equals(Receipt, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Invalid library path: {relative}");
                CheckArchiveEntry(file, relative);
                if (file.FullName.EndsWith('/')) continue;
                if (file.Length > SizeLimit - total) throw new InvalidDataException("The library exceeds 64 MiB.");
                total += file.Length;
                string target = Path.Combine(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                CopyEntry(file, output, buffer);
                if (ScriptFiles.IsScript(relative)) scripts.Add(relative);
            }
            if (!foundFolder && folder.Length > 0) throw new InvalidDataException($"Folder {folder}/ was not found in {repository}.");
            return scripts;
        }

        private static string EntryFor(string stage, List<string> scripts, LibraryManifest manifest)
        {
            if (manifest != null)
            {
                // Match the archive spelling even when the current filesystem ignores case.
                if (!scripts.Contains(manifest.Entry, StringComparer.Ordinal))
                    throw new InvalidDataException($"kscript.json: library.entry '{manifest.Entry}' does not name a script in this library.");
                CheckDestination(stage, Path.Combine(stage, manifest.Entry));
                return manifest.Entry;
            }
            return scripts.OrderBy(s => s == "index.kscript" ? 0 : s == "index.ks.xml" ? 1 : 2).ThenBy(s => s.Count(c => c == '/')).ThenBy(s => s, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidDataException("The selected library contains no .kscript or .ks.xml files.");
        }
    }
}
