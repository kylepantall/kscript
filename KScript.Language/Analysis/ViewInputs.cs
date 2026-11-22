using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>One value a script gives a view: <c>@view('auth/account', data: { user: @user() })</c> gives it $data.</summary>
    public sealed class ViewInput
    {
        public ViewInput(string name, TypeShape shape, string path, LinePosition start, LinePosition end, string function)
        {
            Name = name;
            Shape = shape;
            Path = path;
            Start = start;
            End = end;
            Function = function;
        }

        /// <summary>The variable's name in the view.</summary>
        public string Name { get; }

        public TypeShape Shape { get; }

        /// <summary>The script that calls @view.</summary>
        public string Path { get; }

        /// <summary>Where the value is named in the call (the argument's name, or the map key).</summary>
        public LinePosition Start { get; }

        public LinePosition End { get; }

        /// <summary>The function the call is in (a controller action such as account), or null at the top level.</summary>
        public string Function { get; }
    }

    /// <summary>
    /// Finds what a KScript.Http view is given: every <c>@view('name', ...)</c> in the project's scripts that names the
    /// view, and the shape of each value it passes. A view then knows its variables' types, so <c>$data.</c> and a
    /// loop over <c>$data.tokens</c> offer the fields the controller put there. Scripts that can't mention the view
    /// are skipped without being analysed, and each script's calls are kept until its text changes.
    /// </summary>
    public static class ViewInputs
    {
        private static readonly object Gate = new object();

        private static readonly Dictionary<string, (string Text, List<(string View, ViewInput Input)> Calls)> Cache =
            new Dictionary<string, (string, List<(string, ViewInput)>)>(StringComparer.Ordinal);

        /// <summary>The most scripts whose calls are kept at once.</summary>
        internal static int CacheLimit = 512;

        /// <summary>
        /// The values given to the view at <paramref name="path"/>, by variable name (one entry per call that passes
        /// it). Empty when the file isn't in a project (a folder with kscript.json), or no script renders it.
        /// </summary>
        public static Dictionary<string, List<ViewInput>> Find(string path, IFileSystem files, LanguageCatalog catalog)
        {
            Dictionary<string, List<ViewInput>> found = new Dictionary<string, List<ViewInput>>(StringComparer.Ordinal);
            string full = System.IO.Path.GetFullPath(path);
            string root = ProjectUsage.ProjectRoot(System.IO.Path.GetDirectoryName(full), files);
            if (root == null)
            {
                return found;
            }
            HashSet<string> names = NamesOf(full, root);
            string stem = System.IO.Path.GetFileNameWithoutExtension(full);

            foreach (string file in ProjectUsage.ProjectFiles(root, recursive: true, files))
            {
                if (MarkupParser.KindFromPath(file) != DocumentKind.Script)
                {
                    continue;
                }
                string text = files.ReadText(file);
                if (text == null || text.IndexOf("view(", StringComparison.Ordinal) < 0 || text.IndexOf(stem, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                foreach ((string view, ViewInput input) in CallsIn(file, text, files, catalog))
                {
                    if (names.Contains(view))
                    {
                        if (!found.TryGetValue(input.Name, out List<ViewInput> list))
                        {
                            found[input.Name] = list = new List<ViewInput>();
                        }
                        list.Add(input);
                    }
                }
            }
            return found;
        }

        /// <summary>Forgets every script's cached calls (for tests).</summary>
        internal static void ClearCache()
        {
            lock (Gate)
            {
                Cache.Clear();
            }
        }

        /// <summary>
        /// The names a script can render the view by, as @view takes them: its path in the views folder or the site,
        /// with or without the extension (views/auth/account.html is 'auth/account').
        /// </summary>
        internal static HashSet<string> NamesOf(string view, string root)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string relative = System.IO.Path.GetRelativePath(root, view).Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal))
            {
                return names;
            }
            foreach (string name in new[] { relative, relative.StartsWith("views/", StringComparison.OrdinalIgnoreCase) ? relative.Substring(6) : null })
            {
                if (name == null)
                {
                    continue;
                }
                names.Add(name);
                string extension = System.IO.Path.GetExtension(name);
                if (extension.Length > 0)
                {
                    names.Add(name.Substring(0, name.Length - extension.Length));
                }
            }
            return names;
        }

        private static List<(string View, ViewInput Input)> CallsIn(string file, string text, IFileSystem files, LanguageCatalog catalog)
        {
            string key = System.IO.Path.GetFullPath(file);
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var cached) && cached.Text == text)
                {
                    return cached.Calls;
                }
            }

            List<(string, ViewInput)> calls = new List<(string, ViewInput)>();
            DocumentAnalysis analysis = Analyzer.Analyze(text, file, files, catalog, kind: null, projectWide: false);
            TypeInference inference = new TypeInference(catalog, analysis);
            foreach (ExpressionRegion region in analysis.Regions)
            {
                foreach (CommandCallNode call in region.Nodes.SelectMany(n => n.DescendantsAndSelf()).OfType<CommandCallNode>())
                {
                    if (call.Name != "view" || call.Arguments.Count == 0 || call.Arguments[0].Value is not LiteralNode { Kind: LiteralKind.String } viewName)
                    {
                        continue;
                    }
                    string view = viewName.Value.Replace('\\', '/').Trim().TrimStart('/');
                    string function = FunctionOf(region.Element);
                    // @view('people/show', { person: $p }) spreads a map's keys; named values are given by name.
                    ArgumentNode spread = call.Arguments.Skip(1).FirstOrDefault(a => a.Name == null);
                    if (spread?.Value is MapNode map)
                    {
                        foreach (MapEntryNode entry in map.Entries)
                        {
                            calls.Add((view, Input(analysis, region, entry.Key, inference.Infer(entry.Value, region), entry.KeySpan, file, function)));
                        }
                    }
                    foreach (ArgumentNode argument in call.Arguments.Where(a => a.Name != null))
                    {
                        calls.Add((view, Input(analysis, region, argument.Name, inference.Infer(argument.Value, region), argument.NameSpan, file, function)));
                    }
                }
            }

            lock (Gate)
            {
                if (Cache.Count >= CacheLimit)
                {
                    Cache.Clear();
                }
                Cache[key] = (text, calls);
            }
            return calls;
        }

        private static ViewInput Input(DocumentAnalysis analysis, ExpressionRegion region, string name, TypeShape shape, TextSpan span, string file, string function)
        {
            TextSpan source = region.Source.ToSource(span);
            return new ViewInput(name, shape, file, analysis.Source.GetPosition(source.Start), analysis.Source.GetPosition(source.End), function);
        }

        private static string FunctionOf(MarkupElement element)
        {
            for (MarkupElement e = element; e != null; e = e.Parent)
            {
                if (e.Name == "fn" && e.GetAttributeValue("name") is string name)
                {
                    return name.Trim();
                }
            }
            return null;
        }
    }
}
