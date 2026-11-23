using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using KScript.Projects;

namespace KScript.Cli;

public static partial class LibCommand
{
    private static async Task<(Source Source, string Commit)> FindPublisherLibrary(
        HttpClient client, string name, string projectDirectory)
    {
        var repositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<(Source Source, string Commit)>();

        async Task Search(string repository, bool listed)
        {
            if (!repositories.Add(repository)) return;
            Source.TryParseDirect(repository, out var root);
            // An owner can publish many unrelated repos. Only look up a commit for a listed repo
            // after it advertises this library. Recheck the chosen location at that immutable SHA.
            if (listed && await LocateLibrary(client, root, name, "HEAD") == null) return;
            string commit = await ResolveCommit(client, root);
            var source = await LocateLibrary(client, root, name, commit);
            if (source != null) matches.Add((source, commit));
        }

        await Search(LibraryRepositories.DefaultRepository, listed: false);
        foreach (string registered in LibraryRepositories.Registered(projectDirectory))
            await Search(registered, listed: false);
        foreach (string owner in LibraryRepositories.PublisherOwners)
            await foreach (string repository in PublicRepositories(client, owner))
                await Search(repository, listed: true);

        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0)
            throw new InvalidDataException($"No library named '{name}' was found in the built-in publishers or this project's registered repositories.");

        var choices = matches.Select(m => m.Source.Repository + "@" + name).Order(StringComparer.Ordinal);
        throw new InvalidDataException($"There are {matches.Count} libraries named '{name}'. Did you mean?{Environment.NewLine}"
            + string.Join(Environment.NewLine, choices.Select(choice => "  " + choice))
            + $"{Environment.NewLine}Run kscript lib add with one of these names.");
    }

    private static async IAsyncEnumerable<string> PublicRepositories(HttpClient client, string owner)
    {
        const int pageSize = 100;
        for (int page = 1; ; page++)
        {
            using var response = await client.GetAsync($"https://api.github.com/users/{owner}/repos?per_page={pageSize}&page={page}");
            if (page == 1 && response.StatusCode == HttpStatusCode.NotFound) yield break;
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
            var entries = document.RootElement;
            if (entries.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"GitHub did not return a repository list for {owner}.");
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("full_name", out var field)
                    || field.ValueKind != JsonValueKind.String
                    || !field.GetString().StartsWith(owner + "/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"GitHub returned an invalid repository in the listing for {owner}.");
                if (entry.TryGetProperty("private", out var hidden) && hidden.ValueKind == JsonValueKind.True) continue;
                if (entry.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True) continue;
                if (!TryParseRepository(field.GetString(), out string repository)) continue;
                yield return repository;
            }
            if (entries.GetArrayLength() < pageSize) yield break;
        }
    }

    private static async Task<Source> LocateLibrary(HttpClient client, Source root, string name, string revision)
    {
        var folder = root with { Folder = "libraries/" + name };
        try
        {
            await ResolveSubmodule(client, folder, revision);
            return folder;
        }
        catch (MissingCatalogEntry)
        {
            return await PublisherRootMatches(client, root, name, revision, discovering: true)
                ? root with { RootPackageName = name } : null;
        }
    }

    private static async Task<JsonDocument> ReadPublisherConfig(HttpClient client, string repository, string revision, string folder = null)
    {
        string prefix = folder == null ? "" : string.Join('/', folder.Split('/').Select(Uri.EscapeDataString)) + "/";
        using var response = await client.GetAsync($"https://raw.githubusercontent.com/{repository}/{revision}/{prefix}{ProjectConfig.FileName}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
    }

    private static async Task<bool> RootReferencesPin(HttpClient client, Source root, Source pinned, string commit)
    {
        if (!root.IsRoot || pinned.Repository != root.Repository || pinned.Ref != "HEAD" || !pinned.CatalogEntry) return false;
        using var project = await ReadPublisherConfig(client, root.Repository, commit);
        if (project == null || LibraryManifest.ReferencedFolder(project.RootElement) != pinned.Folder) return false;
        using var package = await ReadPublisherConfig(client, root.Repository, commit, pinned.Folder);
        return package != null && LibraryManifest.Read(package.RootElement) != null;
    }

    private static async Task<bool> PublisherRootMatches(HttpClient client, Source root, string name, string revision, bool discovering = false)
    {
        static bool Matches(JsonDocument config, string name)
        {
            if (config == null || config.RootElement.ValueKind != JsonValueKind.Object) return false;
            var manifest = config.RootElement;
            return manifest.TryGetProperty("name", out var field) && field.ValueKind == JsonValueKind.String
                && field.GetString() == name && LibraryManifest.Read(manifest) != null;
        }

        try
        {
            using var project = await ReadPublisherConfig(client, root.Repository, revision);
            if (project == null || project.RootElement.ValueKind != JsonValueKind.Object) return false;
            string folder;
            try { folder = LibraryManifest.ReferencedFolder(project.RootElement); }
            catch (ProjectConfigException) when (discovering) { return false; }
            if (folder == null) return Matches(project, name);
            using var package = await ReadPublisherConfig(client, root.Repository, revision, folder);
            return Matches(package, name);
        }
        catch (JsonException) when (discovering)
        {
            // An unrelated application with an unreadable config does not advertise a root package.
            return false;
        }
    }
}
