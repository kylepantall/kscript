using System.Text.Json;
using System.Text.Json.Nodes;

using KScript.Projects;
using KScript.Runtime;

namespace KScript.LanguageServer
{
    public sealed partial class KScriptLanguageServer
    {
        private JsonNode ManifestOptions(JsonNode parameters)
        {
            string path = Workspace.UriToPath(parameters?["textDocument"]?["uri"]?.GetValue<string>());
            if (!string.Equals(Path.GetFileName(path), ProjectConfig.FileName, StringComparison.OrdinalIgnoreCase)) return null;

            var types = new JsonObject();
            foreach (string type in Values.DeclarableTypes.Where(type => type is not ("any" or "fn")).OrderBy(t => t, StringComparer.Ordinal))
            {
                IEnumerable<string> rules = type == "password" ? TypeRules.Attributes.Where(r => r is not ("one_of" or "not")) : TypeRules.Attributes;
                types[type] = new JsonArray(rules.Concat(SemanticTypes.OptionsOf(type)).Distinct().Select(rule => (JsonNode)rule).ToArray());
            }

            var packages = new JsonObject();
            ProjectConfig project = ProjectFor(path);
            if (project?.Libraries.Count > 0)
            {
                string folder = Path.GetFullPath(project.PathFor("lib"), project.SiteRoot);
                foreach (LibraryDependency dependency in project.Libraries.Values)
                {
                    string receipt = Path.Combine(folder, dependency.Name, LibraryDependency.ReceiptFileName);
                    foreach (string package in ReceiptPackages(receipt, dependency))
                    {
                        string runtime = service.Catalog.FindPackage(package);
                        if (runtime == null || runtime == "KScript.Core") continue;
                        if (packages[runtime] is not JsonArray requiredBy) packages[runtime] = requiredBy = new JsonArray();
                        if (!requiredBy.Any(name => name.GetValue<string>() == dependency.Name)) requiredBy.Add((JsonNode)dependency.Name);
                    }
                }
            }
            return new JsonObject { ["types"] = types, ["requiredPackages"] = packages };
        }

        private IEnumerable<string> ReceiptPackages(string path, LibraryDependency dependency)
        {
            try
            {
                string text = workspace.ReadText(path);
                if (text == null) return Array.Empty<string>();
                using JsonDocument json = JsonDocument.Parse(text);
                JsonElement root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("commit", out JsonElement commit) || commit.ValueKind != JsonValueKind.String || commit.GetString() != dependency.Commit
                    || !root.TryGetProperty("source", out JsonElement source) || source.ValueKind != JsonValueKind.String || source.GetString() != dependency.Source
                    || !root.TryGetProperty("packages", out JsonElement packages) || packages.ValueKind != JsonValueKind.Array)
                    return Array.Empty<string>();
                return packages.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return Array.Empty<string>();
            }
        }
    }
}
