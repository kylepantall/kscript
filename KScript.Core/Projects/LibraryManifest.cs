using System.Text.Json;
using System.Text.RegularExpressions;
using KScript.Runtime;

namespace KScript.Projects;

/// <summary>The installable library declared by a kscript.json, independent of the consuming project's settings.</summary>
public sealed record LibraryManifest(string Name, string Version, string Entry, IReadOnlyList<string> Packages)
{
    public static LibraryManifest Load(string path)
    {
        using var document = ProjectConfig.ParseJson(File.ReadAllText(path), path);
        return Read(document.RootElement);
    }

    public static LibraryManifest Parse(string json, string path, out string folder)
    {
        using var document = ProjectConfig.ParseJson(json, path);
        folder = ReferencedFolder(document.RootElement);
        return folder == null ? Read(document.RootElement) : null;
    }

    /// <summary>A project can point to the folder containing its package's own manifest.</summary>
    public static string ReferencedFolder(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("library", out var library)
            || library.ValueKind != JsonValueKind.String) return null;
        string folder = library.GetString();
        if (!folder.Split('/').All(LibraryDependency.IsSafePathComponent))
            throw new ProjectConfigException("library must name a relative package folder inside the project.");
        return folder;
    }

    public static LibraryManifest Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new ProjectConfigException("kscript.json must contain an object.");
        if (!root.TryGetProperty("library", out var library)) return null;
        if (library.ValueKind != JsonValueKind.Object)
            throw new ProjectConfigException("library must contain an entry and a version.");
        foreach (var property in library.EnumerateObject())
            if (property.Name is not ("entry" or "version"))
                throw new ProjectConfigException($"Unknown library setting '{property.Name}'. Expected entry or version.");

        string name = Required(root, "name", "name");
        if (!Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z") || !LibraryDependency.IsSafePathComponent(name))
            throw new ProjectConfigException("A library name must be a portable folder name starting with a letter or digit and using only letters, digits, dots, hyphens and underscores.");

        string entry = Required(library, "entry", "library.entry");
        if (!entry.Split('/').All(LibraryDependency.IsSafePathComponent) ||
            !(entry.EndsWith(".kscript", StringComparison.OrdinalIgnoreCase) || entry.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase)))
            throw new ProjectConfigException("library.entry must be a relative .kscript or .ks.xml path inside the library.");

        string version = Required(library, "version", "library.version");
        var parsed = SemverType.Read(version).Value as NativeText;
        if (parsed == null || parsed.Text != version || ((string)parsed.Part("prerelease")).Split('.')
            .Any(id => id.Length > 1 && id[0] == '0' && id.All(char.IsAsciiDigit)))
            throw new ProjectConfigException("library.version must be a semantic version, such as 1.0.0 or 2.0.0-beta.1.");

        var packages = new List<string>();
        if (root.TryGetProperty("packages", out var declared))
        {
            if (declared.ValueKind != JsonValueKind.Array)
                throw new ProjectConfigException("packages must be an array of runtime package names.");
            foreach (var item in declared.EnumerateArray())
            {
                string package = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                string known = PackageRegistry.Default.Names.FirstOrDefault(n =>
                    n.Equals(PackageRegistry.Normalise(package), StringComparison.OrdinalIgnoreCase));
                if (known == null) throw new ProjectConfigException($"Unknown runtime package '{package}' in library '{name}'.");
                if (!packages.Contains(known)) packages.Add(known);
            }
        }
        return new LibraryManifest(name, version, entry, packages);
    }

    private static string Required(JsonElement parent, string property, string label) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() :
        throw new ProjectConfigException($"{label} must be a nonempty string for an installable library.");
}
