using KScript.Metadata;
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

        #region Surfaces

        public const string RenderMethod = "render";

        /// <summary>The element a component's render draws the caller's content with.</summary>
        public const string SlotElement = "slot";
        public const string Page = "page";
        public const string Window = "window";
        public const string Terminal = "terminal";
        public const string Document = "document";

        /// <summary>Where a component can be drawn. A component has at most one <c>render</c> for each.</summary>
        public static IReadOnlyList<string> Surfaces { get; } = new[] { Page, Window, Terminal, Document };

        /// <summary>The packages whose elements show which surface a <c>render</c> draws.</summary>
        private static readonly Dictionary<string, string> SurfacePackages = new(StringComparer.Ordinal)
        {
            ["KScript.UI"] = Window,
            ["KScript.Terminal"] = Terminal,
            ["KScript.Document"] = Document,
        };

        /// <summary>The surface a package's elements draw (a window for KScript.UI), or null.</summary>
        public static string SurfaceOfElement(string package) =>
            package != null && SurfacePackages.TryGetValue(package, out string surface) ? surface : null;

        /// <summary>How each surface reads in a message: "a window", "the terminal".</summary>
        public static string SurfacePhrase(string surface) => surface switch
        {
            Page => "a page",
            Window => "a window",
            Document => "a document",
            _ => "the terminal",
        };

        /// <summary>True for a <c>fn render</c> directly inside a component.</summary>
        public static bool IsRender(MarkupElement fn) =>
            fn?.Name == "fn" && fn.GetAttributeValue("name")?.Trim() == RenderMethod && IsComponentType(fn.Parent);

        /// <summary>
        /// The surface a component's <c>render</c> draws, or null when its content doesn't show one. It is named
        /// (<c>fn render(window)</c>), or read from the content: an <c>html</c> block is a page, and otherwise the
        /// first element that belongs to a surface decides (<c>Column</c> a window, <c>echo</c> the terminal).
        /// <paramref name="packageOf"/> gives the package an element belongs to.
        /// </summary>
        public static string SurfaceOf(MarkupElement render, Func<string, string> packageOf)
        {
            if (render.GetAttributeValue("surface")?.Trim() is string named) return named;
            if (render.GetAttributeValue("returns")?.Trim() == Contract) return Page;
            var value = render.GetAttribute("value")?.Value
                ?? render.ChildElements.LastOrDefault(e => e.Name == "return")?.GetAttribute("value")?.Value;
            if (value != null && LeadingBlock(value) is StringBlock block) return block.Language == "html" ? Page : null;
            foreach (MarkupElement e in Within(render))
            {
                if (e.Name == "echo") return Terminal;
                if (SurfaceOfElement(packageOf(ElementInfo.BareName(e.Name))) is string surface) return surface;
            }
            return null;
        }

        private static IEnumerable<MarkupElement> Within(MarkupElement element) =>
            element.ChildElements.SelectMany(child => Within(child).Prepend(child));

        /// <summary>The name a type keeps a surface's <c>render</c> under. The page's stays <c>render</c>, which pages call.</summary>
        public static string RenderKey(string surface) => surface == Page ? RenderMethod : RenderMethod + "(" + surface + ")";

        /// <summary>What to tell someone whose <c>render</c> doesn't show its surface.</summary>
        public static string NameTheSurface(string component) =>
            $"{component}'s render doesn't show where it draws. Name it: fn render(window), fn render(terminal) or fn render(document).";

        public const string SlotOutsideRender = "<slot> only works in a component's render, where it draws what was written inside the component.";

        public static string TwoRenders(string component, string surface) =>
            $"{component} has two render methods for {SurfacePhrase(surface)}. Keep one.";

        /// <summary>What to tell someone who draws a component where it has no <c>render</c>.</summary>
        public static string NoRenderFor(string component, string surface) =>
            $"{component} has no render for {SurfacePhrase(surface)}. Add one to the component: fn render({surface}) {{ }}.";

        #endregion

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
            if (value == null || Returns(function) != contract) return null;
            return LeadingBlock(value);
        }

        /// <summary>
        /// What a method returns. A component's <c>render</c> and <c>style</c> need not say: their content shows it.
        /// </summary>
        public static string Returns(MarkupElement function) =>
            function?.GetAttributeValue("returns")?.Trim() ?? (!IsComponentType(function?.Parent) ? null
                : function.GetAttributeValue("name")?.Trim() switch { RenderMethod => Contract, "style" => StyleContract, _ => null });

        /// <summary>The ``` block a value is made of, or null when it is anything else.</summary>
        private static StringBlock LeadingBlock(DecodedText value)
        {
            string text = value.Value;
            int start = 0;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            var block = StringBlock.Read(text, start);
            return block != null && string.IsNullOrWhiteSpace(text[block.End..]) ? block : null;
        }
    }
}
