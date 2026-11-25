using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>The script and HTML conventions shared by the renderer, scaffolder and editor.</summary>
    public static class ComponentScript
    {
        public const string Contract = "Component";
        public const string StyleContract = "ComponentStyle";

        public static bool IsComponentType(MarkupElement type) =>
            type?.Name == "type" && type.GetAttributeValue("extends") == Contract;

        /// <summary>The declarations a component may keep inside its own block, for the types only it uses.</summary>
        public static IReadOnlyList<string> NestedDeclarations { get; } = new[] { "type", "model", "enum", "error" };

        /// <summary>True for a type, model, enum or error declared inside a component.</summary>
        public static bool IsNestedDeclaration(MarkupElement e) =>
            e != null && NestedDeclarations.Contains(e.Name) && IsComponentType(e.Parent);

        /// <summary>The message for a declaration nested in a type that is not a component.</summary>
        public static string NestedOnlyInComponent(MarkupElement nested) =>
            $"Only a component can hold its own types, so {nested.Name} {nested.GetAttributeValue("name")?.Trim()} can't sit inside {nested.Parent.Name} {nested.Parent.GetAttributeValue("name")?.Trim()}. Give it its own file.";

        public static IReadOnlyList<string> Extensions { get; } = new[] { ScriptFiles.Code, ScriptFiles.Xml, ".html", ".md" };

        public static string Tag(string path)
        {
            string file = Path.GetFileName(path);
            string extension = Extensions.FirstOrDefault(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            return extension == null ? file : file[..^extension.Length];
        }

        public static string FindFile(string folder, string tag, Func<string, bool> exists, Func<string, string> read)
        {
            var candidates = Paths(folder, tag).Where(exists).ToArray();
            var script = candidates.FirstOrDefault(ScriptFiles.IsScript);
            var legacy = candidates.FirstOrDefault(path => !ScriptFiles.IsScript(path));
            if (legacy == null || script == null) return script ?? legacy;
            // A legacy template can have a same-named companion script. Only a component class takes precedence.
            return candidates.Where(ScriptFiles.IsScript).FirstOrDefault(path => read(path) is string text
                && FindType(MarkupParser.Parse(new SourceText(text), DocumentKind.Script, path), tag) != null) ?? legacy;
        }

        public static IEnumerable<string> Paths(string folder, string tag) =>
            Extensions.Select(ext => Path.Combine(folder, tag.ToLowerInvariant() + ext));

        public static MarkupElement FindType(MarkupDocument script, string tag)
        {
            string name = tag.Replace("-", "").Replace("_", "");
            return script.RootElements.SelectMany(e => e.Name == "kscript" ? e.ChildElements : new[] { e })
                .FirstOrDefault(e => IsComponentType(e)
                    && (string.Equals(e.GetAttributeValue("name"), name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.GetAttributeValue("name"), name + Contract, StringComparison.OrdinalIgnoreCase)));
        }

        public static StringBlock HtmlBlock(MarkupElement function, DecodedText value) =>
            ReturnBlock(function, value, Contract) is { Language: "html" } html ? html : null;

        public static StringBlock StyleBlock(MarkupElement function, DecodedText value) =>
            ReturnBlock(function, value, StyleContract) is { Language: "css" or "ks.css" } style ? style : null;

        private static StringBlock ReturnBlock(MarkupElement function, DecodedText value, string contract)
        {
            if (function?.GetAttributeValue("returns")?.Trim() != contract || value == null) return null;
            string text = value.Value;
            int start = 0;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            var block = StringBlock.Read(text, start);
            return block != null && string.IsNullOrWhiteSpace(text[block.End..]) ? block : null;
        }
    }
}
