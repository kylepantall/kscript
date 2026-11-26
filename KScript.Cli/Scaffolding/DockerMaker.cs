using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

using KScript.Projects;
using KScript.Updates;

namespace KScript.Scaffolding;

internal static class DockerMaker
{
    internal const string Help = "kscript make docker [--dir <project>] [--dry-run] [--force]";
    internal const string SourceHint = "For an unpublished KScript build: KSCRIPT_SOURCE=/path/to/kscript docker compose up --build";

    internal static Dictionary<string, string> Render(ProjectConfig project)
    {
        var assembly = typeof(DockerMaker).Assembly;
        string version = SelfUpdater.CurrentVersion;
        string revision = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "KScriptPublishedRevision")?.Value;
        var values = new Dictionary<string, string>
        {
            ["port"] = (project?.Serve.Port ?? 8080).ToString(CultureInfo.InvariantCulture),
            ["version"] = version,
            ["source"] = SourceContext(revision),
        };

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in new[] { "Dockerfile", "compose.yaml", "dot-dockerignore" })
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream("KScript.Scaffolding.Docker/" + file));
            files[file == "dot-dockerignore" ? ".dockerignore" : file] = StubTemplate.Render(reader.ReadToEnd(), values);
        }
        return files;
    }

    internal static string SourceContext(string revision) => revision?.Length == 40 && revision.All(Uri.IsHexDigit)
        ? "${KSCRIPT_SOURCE:-https://github.com/" + SelfUpdater.DefaultRepository + ".git#" + revision + "}"
        : "${KSCRIPT_SOURCE:?Set KSCRIPT_SOURCE to the path of the KScript checkout used to build this CLI}";

    internal static IReadOnlyList<string> Make(string folder, bool force, bool dryRun)
    {
        ProjectConfig project = ProjectConfig.FindAndLoad(folder);
        string root = project?.Directory ?? Path.GetFullPath(folder);
        var files = Render(project);
        foreach (string file in files.Keys)
        {
            string path = Path.Combine(root, file);
            if (Directory.Exists(path) || File.Exists(path) && !force)
                throw new StubException($"Cannot create {file}: the path already exists. Move it first, or use --force to replace files. " +
                    "To update Docker files made before 0.27 by hand, see \"Docker files from before 0.27\" in docs/projects.md.");
        }
        if (!dryRun)
            foreach (var (file, content) in files) File.WriteAllText(Path.Combine(root, file), content);
        return files.Keys.ToArray();
    }
}
