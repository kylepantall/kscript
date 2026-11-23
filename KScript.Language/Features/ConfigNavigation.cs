using System.Text;
using System.Text.Json;
using KScript.Language.Analysis;
using KScript.Projects;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace KScript.Language.Features;

public sealed partial class LanguageService
{
    private sealed record ConfigKey(string Name, string Type, SourceLocation Location);
    private sealed record KeySpan(TextSpan Span, string Type);

    private ConfigKey ConfigField(DocumentAnalysis analysis, ExpressionRegion region, MemberNode member)
    {
        var names = new List<string>();
        ExprNode target = member;
        while (target is MemberNode field)
        {
            names.Add(field.Name);
            target = field.Target;
        }
        if (analysis.Path == null || target is not VariableNode { Name: "config" } variable ||
            region.Scope.Lookup("config", region.Source.ToSource(variable.Span.Start))?.Kind != SymbolKind.BuiltIn) return null;
        names.Reverse();
        string scriptDir = Path.GetDirectoryName(Path.GetFullPath(analysis.Path));
        string projectDir = ProjectFolder(analysis.Path);
        string manifest = Path.Combine(projectDir, ProjectConfig.FileName);
        try
        {
            var project = Files.ReadText(manifest) is string json ? ProjectConfig.Parse(json, manifest, Files.FileExists, Files.ReadText) : null;
            var options = project?.ConfigOptions() ?? new ConfigOptions { Environment = Environment.GetEnvironmentVariable("KSCRIPT_ENV") };
            var layers = ConfigLoader.ProjectLayers(options, options.Root ?? scriptDir, options.Environment, Files.FileExists, out _);
            ConfigKey found = null;
            foreach (var layer in layers.Where(l => !l.EnvFile))
            {
                string path = Path.GetFullPath(layer.Path);
                string relative = Path.GetRelativePath(projectDir, path);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                string text = Files.ReadText(path);
                if (text == null) continue;
                var key = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? JsonKey(text, names) : YamlKey(text, names);
                if (!key.Supplied) continue;
                if (key.Key == null) { found = null; continue; } // A later scalar replaced a map that held this key.
                var source = new SourceText(text);
                var location = new SourceLocation(path, source.GetPosition(key.Key.Span.Start), source.GetPosition(key.Key.Span.End));
                found = new ConfigKey("$config." + string.Join(".", names), key.Key.Type, location);
            }
            return found;
        }
        catch (Exception ex) when (ex is ProjectConfigException or JsonException or YamlException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static (bool Supplied, KeySpan Key) JsonKey(string text, IReadOnlyList<string> names)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return default;
        return FindJsonKey(ref reader, bytes, names, 0);
    }

    private static (bool, KeySpan) FindJsonKey(ref Utf8JsonReader reader, byte[] bytes, IReadOnlyList<string> names, int depth)
    {
        (bool, KeySpan) found = default;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool matches = names[depth].Equals(reader.GetString(), StringComparison.OrdinalIgnoreCase);
            // TokenStartIndex and ValueSpan use UTF-8, while editor columns count UTF-16 code units.
            int start = checked((int)reader.TokenStartIndex) + 1;
            int length = reader.ValueSpan.Length;
            reader.Read();
            if (matches && depth == names.Count - 1)
            {
                string type = reader.TokenType switch
                {
                    JsonTokenType.StartObject => "map", JsonTokenType.StartArray => "list", JsonTokenType.String => "string",
                    JsonTokenType.Number => "number", JsonTokenType.Null => "null", _ => "bool",
                };
                found = (true, new KeySpan(new TextSpan(Encoding.UTF8.GetCharCount(bytes, 0, start), Encoding.UTF8.GetCharCount(bytes, start, length)), type));
            }
            else if (matches && reader.TokenType == JsonTokenType.StartObject)
            {
                found = FindJsonKey(ref reader, bytes, names, depth + 1);
                continue;
            }
            else if (matches) found = (true, null);
            reader.Skip();
        }
        return found;
    }

    private static (bool Supplied, KeySpan Key) YamlKey(string text, IReadOnlyList<string> names)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root) return default;
        YamlNode node = root;
        for (int index = 0; index < names.Count; index++)
        {
            string name = names[index];
            if (node is not YamlMappingNode map) return (true, null);
            var entry = YamlEntry(map, name, new HashSet<YamlNode>(ReferenceEqualityComparer.Instance));
            if (entry.Key == null) return default;
            node = entry.Value;
            if (index == names.Count - 1)
            {
                var key = (YamlScalarNode)entry.Key;
                int quote = key.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted ? 1 : 0;
                var span = TextSpan.FromBounds((int)key.Start.Index + quote, (int)key.End.Index - quote);
                string type = node switch
                {
                    YamlMappingNode => "map", YamlSequenceNode => "list",
                    YamlScalarNode { Style: ScalarStyle.Plain } scalar => Values.TypeOf(Yaml.PlainScalar(scalar.Value)),
                    _ => "string",
                };
                return (true, new KeySpan(span, type));
            }
        }
        return default;
    }

    private static KeyValuePair<YamlNode, YamlNode> YamlEntry(YamlMappingNode map, string name, HashSet<YamlNode> seen)
    {
        if (!seen.Add(map)) return default;
        var direct = map.Children.LastOrDefault(p => p.Key is YamlScalarNode key && name.Equals(key.Value, StringComparison.OrdinalIgnoreCase));
        if (direct.Key != null) return direct;
        var merged = map.Children.FirstOrDefault(p => p.Key is YamlScalarNode { Value: "<<" }).Value;
        IEnumerable<YamlNode> sources = merged is YamlSequenceNode sequence ? sequence.Children : new[] { merged };
        foreach (var source in sources.OfType<YamlMappingNode>())
        {
            var inherited = YamlEntry(source, name, seen);
            if (inherited.Key != null) return inherited;
        }
        return default;
    }
}
