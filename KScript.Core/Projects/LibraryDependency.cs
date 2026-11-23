using System.Text.Json;
using System.Text.RegularExpressions;

namespace KScript.Projects;

/// <summary>An installed source library and the immutable revision its consumer expects.</summary>
public sealed record LibraryDependency(string Name, string Source, string Commit)
{
    public static bool IsSafePathComponent(string part) => !string.IsNullOrEmpty(part) &&
        !part.EndsWith('.') && !part.EndsWith(' ') && !part.Any(char.IsControl) &&
        part.IndexOfAny("/\\<>:\"|?*".ToCharArray()) < 0 && !part.Equals(".git", StringComparison.OrdinalIgnoreCase) &&
        !Regex.IsMatch(part, @"\A(?:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public void Validate()
    {
        if (!IsSafePathComponent(Name) || Name.StartsWith('.'))
            throw new ProjectConfigException($"Invalid library directory name '{Name}'. Use a single portable folder name that does not start with a dot.");
        if (string.IsNullOrWhiteSpace(Source))
            throw new ProjectConfigException($"libraries.{Name}.source must be a nonempty string.");
        if (Commit == null || !Regex.IsMatch(Commit, @"\A[0-9a-f]{40}\z"))
            throw new ProjectConfigException($"libraries.{Name}.commit must be a full 40-character lowercase hexadecimal Git commit.");
    }

    /// <summary>Reads only the dependency table, without resolving standards or other project settings.</summary>
    public static IReadOnlyDictionary<string, LibraryDependency> ReadAll(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new ProjectConfigException("kscript.json must contain an object.");
        var dependencies = new Dictionary<string, LibraryDependency>(StringComparer.OrdinalIgnoreCase);
        bool found = false;
        foreach (var table in root.EnumerateObject())
        {
            if (!table.NameEquals("libraries")) continue;
            if (found) throw new ProjectConfigException("Duplicate libraries setting. Put all library dependencies in one object.");
            found = true;
            if (table.Value.ValueKind != JsonValueKind.Object)
                throw new ProjectConfigException("libraries must be an object keyed by library directory name.");
            foreach (var entry in table.Value.EnumerateObject())
            {
                string name = entry.Name;
                if (entry.Value.ValueKind != JsonValueKind.Object)
                    throw new ProjectConfigException($"libraries.{name} must be an object with source and commit strings.");
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var field in entry.Value.EnumerateObject())
                {
                    if (field.Name is not ("source" or "commit"))
                        throw new ProjectConfigException($"Unknown libraries.{name}.{field.Name} setting. Expected source or commit.");
                    if (field.Value.ValueKind != JsonValueKind.String)
                        throw new ProjectConfigException($"libraries.{name}.{field.Name} must be a string.");
                    if (!fields.TryAdd(field.Name, field.Value.GetString()))
                        throw new ProjectConfigException($"Duplicate libraries.{name}.{field.Name} setting.");
                }
                var dependency = new LibraryDependency(name, fields.GetValueOrDefault("source"), fields.GetValueOrDefault("commit"));
                dependency.Validate();
                if (!dependencies.TryAdd(name, dependency))
                    throw new ProjectConfigException($"Duplicate library directory '{name}'. Every name in libraries must be unique ignoring case.");
            }
        }
        return dependencies;
    }
}
