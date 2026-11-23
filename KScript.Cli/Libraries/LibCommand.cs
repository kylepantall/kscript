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

namespace KScript.Cli
{
    /// <summary>Installs a GitHub library as ordinary source files imported by their project-relative paths.</summary>
    public static class LibCommand
    {
        private const string Receipt = ".kscript-lib.json";
        private const long SizeLimit = 64L * 1024 * 1024;
        private const string Usage = "Usage: kscript lib add <owner/repo | GitHub URL>";

        public static int Run(string[] args, TextWriter output, TextWriter error) =>
            Run(args, output, error, WorkingFolder.Current, new HttpClientHandler { AllowAutoRedirect = false });

        internal static int Run(string[] args, TextWriter output, TextWriter error, string workingDirectory, HttpMessageHandler handler)
        {
            using var client = new HttpClient(handler) { MaxResponseContentBufferSize = SizeLimit };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("kscript-lib/1");
            if (args.SequenceEqual(new[] { "add", "--help" }) || args.SequenceEqual(new[] { "add", "-h" }))
            {
                CliReference.PrintCommand(output, CliReference.Find("lib"));
                return ExitCodes.Ok;
            }
            if (args.Length != 2 || args[0] != "add" || !Source.TryParse(args[1], out var source))
            {
                error.WriteLine(Usage);
                return ExitCodes.Usage;
            }
            try
            {
                ProjectConfig project = ProjectConfig.FindAndLoad(workingDirectory);
                string root = project?.Directory ?? workingDirectory;
                string libraryFolder = Path.GetFullPath(Path.Combine(project?.SiteRoot ?? root, project?.PathFor("lib") ?? "lib"));
                CheckDestination(root, libraryFolder);
                Install(client, source, libraryFolder, project?.SiteRoot ?? root, output).GetAwaiter().GetResult();
                return ExitCodes.Ok;
            }
            catch (ProjectConfigException ex) { error.WriteLine(ex.Message); return ExitCodes.Usage; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or JsonException)
            {
                error.WriteLine($"Could not add {source.Name}: {ex.Message}");
                return ExitCodes.Failed;
            }
        }

        private sealed record Source(string Owner, string Repo, string Ref, string Folder)
        {
            internal string Name => Folder.Length == 0 ? Repo : Folder.Split('/')[^1];
            internal string Url => $"https://github.com/{Owner}/{Repo}" + (Ref == null ? "" : "/tree/" + Uri.EscapeDataString(Ref)
                + (Folder.Length == 0 ? "" : "/" + string.Join('/', Folder.Split('/').Select(Uri.EscapeDataString))));

            // GitHub's ordinary tree URLs leave slashes in a branch name unescaped.
            internal Source LongerRef()
            {
                int slash = Folder.IndexOf('/');
                return this with { Ref = Ref + "/" + (slash < 0 ? Folder : Folder[..slash]), Folder = slash < 0 ? "" : Folder[(slash + 1)..] };
            }

            internal static bool TryParse(string input, out Source source)
            {
                source = null;
                const string host = "https://github.com/";
                if (input.Contains('#') || input.Contains('?')) return false;
                bool url = input.StartsWith(host, StringComparison.OrdinalIgnoreCase);
                string[] parts = (url ? input.Substring(host.Length) : input).TrimEnd('/').Split('/');
                if (parts.Length < 2 || (parts.Length != 2 && (!url || parts.Length < 4 || parts[2] != "tree"))) return false;
                string owner = parts[0].ToLowerInvariant(), repo = parts[1].ToLowerInvariant();
                if (repo.EndsWith(".git", StringComparison.Ordinal)) repo = repo[..^4];
                if (!Regex.IsMatch(owner, @"\A[a-z0-9][a-z0-9-]*\z") || !Regex.IsMatch(repo, @"\A[a-z0-9][a-z0-9._-]*\z") || !SafePart(repo)) return false;
                string reference = parts.Length > 2 ? Uri.UnescapeDataString(parts[3]) : null;
                if (reference != null && !reference.Split('/').All(SafePart)) return false;
                string[] folders = parts.Skip(4).Select(Uri.UnescapeDataString).ToArray();
                if (!folders.All(SafePart) || folders.LastOrDefault()?.StartsWith('.') == true) return false;
                source = new Source(owner, repo, reference, string.Join('/', folders));
                return true;
            }
        }

        private static bool SafePart(string part) => part.Length > 0 && !part.EndsWith('.') && !part.EndsWith(' ') &&
            !part.Any(char.IsControl) && part.IndexOfAny("/\\<>:\"|?*".ToCharArray()) < 0 &&
            !part.Equals(".git", StringComparison.OrdinalIgnoreCase) &&
            !Regex.IsMatch(part, @"\A(?:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static void CheckDestination(string root, string destination)
        {
            if (Path.GetRelativePath(root, destination) == ".") return;
            string path = root;
            foreach (string part in Path.GetRelativePath(root, destination).Split(Path.DirectorySeparatorChar))
            {
                if (!SafePart(part)) throw new IOException("Choose a library folder inside the project.");
                path = Path.Combine(path, part);
                if (new DirectoryInfo(path).LinkTarget != null) throw new IOException($"The library path includes a symbolic link: {path}");
            }
        }

        private static async Task Install(HttpClient client, Source source, string libraryFolder, string site, TextWriter output)
        {
            string requested = source.Url;
            bool ambiguous = source.Folder.Length > 0 && !source.Ref.Contains('/');
            // The last URL component names the library if it's a subfolder. If the entire tail
            // names a branch, the library takes the repo name. Check both receipts offline.
            foreach (string name in (ambiguous ? new[] { source.Name, source.Repo } : new[] { source.Name }).Distinct())
            {
                string possible = Path.Combine(libraryFolder, name);
                if (ambiguous && new DirectoryInfo(possible).LinkTarget != null) continue;
                CheckDestination(libraryFolder, possible);
                if (Existing(requested, possible, site, output, probe: ambiguous)) return;
            }
            string repoUrl = $"https://api.github.com/repos/{source.Owner}/{source.Repo}";
            byte[] details;
            while (true)
            {
                try
                {
                    details = await client.GetByteArrayAsync(repoUrl + "/commits/" + Uri.EscapeDataString(source.Ref ?? "HEAD"));
                    break;
                }
                // GitHub also uses 422 for an unknown commit/ref in an existing repo.
                catch (HttpRequestException ex) when (ambiguous && source.Folder.Length > 0 &&
                    ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
                {
                    source = source.LongerRef();
                }
            }
            using var metadata = JsonDocument.Parse(details);
            if (metadata.RootElement.ValueKind != JsonValueKind.Object || !metadata.RootElement.TryGetProperty("sha", out var sha)
                || sha.ValueKind != JsonValueKind.String || !IsCommit(sha.GetString()))
                throw new InvalidDataException("GitHub did not return a commit SHA.");
            string commit = sha.GetString();
            string destination = Path.Combine(libraryFolder, source.Name);
            CheckDestination(libraryFolder, destination);
            if (Existing(source.Url, destination, site, output)) return;
            byte[] bytes = await client.GetByteArrayAsync($"https://codeload.github.com/{source.Owner}/{source.Repo}/zip/{commit}");
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            string stage = Path.Combine(parent, ".kscript-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                string entry = Extract(archive, source.Folder, stage);
                var receipt = new JsonObject { ["source"] = source.Url, ["commit"] = commit, ["entry"] = entry };
                if (requested != source.Url) receipt["requested"] = requested;
                File.WriteAllText(Path.Combine(stage, Receipt), receipt.ToJsonString() + "\n");
                try { Directory.Move(stage, destination); }
                catch (IOException) when (Directory.Exists(destination)) { if (Existing(source.Url, destination, site, output)) return; throw; }
                Report(output, "Installed", commit, site, destination, entry);
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
        }

        private static bool IsCommit(string value) => value != null && Regex.IsMatch(value, @"\A[0-9a-f]{40}\z");

        private static bool Existing(string source, string destination, string site, TextWriter output, bool probe = false)
        {
            string receipt = Path.Combine(destination, Receipt);
            if (!Directory.Exists(destination) && !File.Exists(destination)) return false;
            if (File.Exists(receipt) && new FileInfo(receipt).LinkTarget == null)
            {
                JsonObject saved;
                try { saved = JsonNode.Parse(File.ReadAllText(receipt)) as JsonObject; }
                catch (JsonException) when (probe) { return false; }
                string entry = saved?["entry"]?.ToString(), commit = saved?["commit"]?.ToString();
                if ((saved?["source"]?.ToString() == source || saved?["requested"]?.ToString() == source)
                    && IsCommit(commit) && entry != null && entry.Split('/').All(SafePart))
                {
                    CheckDestination(destination, Path.Combine(destination, entry));
                    if (!File.Exists(Path.Combine(destination, entry))) throw new IOException($"The installed library is missing {entry}.");
                    Report(output, "Already installed", commit, site, destination, entry);
                    return true;
                }
            }
            if (probe) return false;
            throw new IOException($"{destination} already exists. Move it before installing this library.");
        }

        private static void Report(TextWriter output, string action, string commit, string site, string destination, string entry)
        {
            output.WriteLine($"{action} {Path.GetFileName(destination)} at {commit}.");
            output.WriteLine($"From a script in {site}: import \"{Path.GetRelativePath(site, Path.Combine(destination, entry)).Replace('\\', '/')}\"");
        }

        private static string Extract(ZipArchive archive, string folder, string stage)
        {
            var scripts = new List<string>();
            string prefix = folder.Length == 0 ? "" : folder + "/";
            byte[] buffer = new byte[81920];
            long total = 0;
            foreach (var file in archive.Entries)
            {
                // GitHub prefixes every archive path with a generated root directory.
                int slash = file.FullName.IndexOf('/');
                if (slash < 1 || !SafePart(file.FullName[..slash])) throw new InvalidDataException("Invalid GitHub archive root.");
                string path = file.FullName[(slash + 1)..];
                if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string relative = path[prefix.Length..].TrimEnd('/');
                if (relative.Length == 0) continue;
                if (!relative.Split('/').All(SafePart) || relative.Split('/')[0].Equals(Receipt, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Invalid library path: {relative}");
                int kind = (file.ExternalAttributes >> 16) & 0xf000;
                if ((file.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || (kind != 0 && kind != 0x8000 && kind != 0x4000))
                    throw new InvalidDataException($"Unsupported link or special file: {relative}");
                if (file.FullName.EndsWith('/')) continue;
                if (file.Length > SizeLimit - total) throw new InvalidDataException("The library exceeds 64 MiB.");
                total += file.Length;
                string target = Path.Combine(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using var input = file.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                long remaining = file.Length;
                while (remaining > 0)
                {
                    int read = input.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                    if (read == 0) throw new InvalidDataException($"Incomplete library file: {relative}");
                    output.Write(buffer, 0, read);
                    remaining -= read;
                }
                if (input.ReadByte() != -1) throw new InvalidDataException($"Incorrect size for library file: {relative}");
                if (relative.EndsWith(".kscript", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase))
                    scripts.Add(relative);
            }
            return scripts.OrderBy(s => s == "index.kscript" ? 0 : s == "index.ks.xml" ? 1 : 2).ThenBy(s => s.Count(c => c == '/')).ThenBy(s => s, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidDataException("The selected library contains no .kscript or .ks.xml files.");
        }
    }
}
