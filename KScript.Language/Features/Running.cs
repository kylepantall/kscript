using KScript.Language.Analysis;
using KScript.Projects;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    /// <summary>A test or a group of tests in a test file, for the editor's test list.</summary>
    public sealed class TestItem
    {
        public TestItem(string name, string fullName, bool isGroup, TextSpan span, TextSpan nameSpan)
        {
            Name = name;
            FullName = fullName;
            IsGroup = isGroup;
            Span = span;
            NameSpan = nameSpan;
        }

        public string Name { get; }

        /// <summary>The groups and the name joined with " › ", as <c>kscript test</c> prints it and <c>--filter</c> matches it.</summary>
        public string FullName { get; }

        /// <summary>True for a describe group.</summary>
        public bool IsGroup { get; }

        /// <summary>True for a test inside a <c>for</c> loop, where its name is filled in for each item, so one test here runs as several.</summary>
        public bool IsTemplate { get; init; }

        public TextSpan Span { get; }
        public TextSpan NameSpan { get; }
        public List<TestItem> Children { get; } = new List<TestItem>();
    }

    /// <summary>A line of text above some code that runs a command when clicked: "Run", "Run test".</summary>
    public sealed class CodeLens
    {
        public CodeLens(TextSpan span, string title, string command, params string[] arguments)
        {
            Span = span;
            Title = title;
            Command = command;
            Arguments = arguments;
        }

        public TextSpan Span { get; }
        public string Title { get; }

        /// <summary>The editor command to run. The document's URI is its first argument, then <see cref="Arguments"/>.</summary>
        public string Command { get; }

        public IReadOnlyList<string> Arguments { get; }

        /// <summary>For a lens about a name ("3 uses"): where the name is, sent as a position after the URI.</summary>
        public TextSpan? Target { get; init; }
    }

    public sealed partial class LanguageService
    {
        public const string RunCommand = "kscript.run";
        public const string RunTestCommand = "kscript.runTest";
        public const string RunTestsInFileCommand = "kscript.runTestsInFile";
        public const string ShowUsesCommand = "kscript.showReferences";

        /// <summary>The tests of a test file, in document order, with describe groups as parents. Empty for other files.</summary>
        public List<TestItem> GetTests(DocumentAnalysis analysis)
        {
            List<TestItem> tests = new List<TestItem>();
            MarkupElement root = analysis.Kind == DocumentKind.Script && TestSettings.IsTestFileName(analysis.Path) ? analysis.Markup.RootElements.FirstOrDefault() : null;
            if (root != null)
            {
                CollectTests(root, string.Empty, tests);
            }
            return tests;
        }

        private static void CollectTests(MarkupElement parent, string prefix, List<TestItem> into, bool inLoop = false)
        {
            foreach (MarkupElement e in parent.ChildElements)
            {
                bool group = e.Name == "describe";
                if (e.Name == "for")
                {
                    // for case in $cases { test(name: "… {$case.x}") { } }: one test per item.
                    CollectTests(e, prefix, into, inLoop: true);
                    continue;
                }
                if (!group && e.Name != "test")
                {
                    continue;
                }
                MarkupAttribute nameAttr = e.GetAttribute("name");
                string name = nameAttr?.ValueText?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                string fullName = prefix.Length > 0 ? prefix + " › " + name : name;
                TestItem item = new TestItem(name, fullName, group, e.Span, nameAttr.Value?.SourceSpan ?? e.NameSpan)
                {
                    IsTemplate = !group && inLoop,
                };
                into.Add(item);
                if (group)
                {
                    CollectTests(e, fullName, item.Children, inLoop);
                }
            }
        }

        /// <summary>
        /// "Run" above the first statement of a script, and "Run test" / "Run tests" above each test and group of a
        /// test file. Pages, components and files with nothing to run get none.
        /// </summary>
        public List<CodeLens> GetCodeLenses(DocumentAnalysis analysis)
        {
            List<CodeLens> lenses = new List<CodeLens>();
            MarkupElement root = analysis.Kind == DocumentKind.Script ? analysis.Markup.RootElements.FirstOrDefault() : null;
            if (root == null)
            {
                return lenses;
            }

            if (TestSettings.IsTestFileName(analysis.Path))
            {
                List<TestItem> tests = GetTests(analysis);
                if (tests.Count > 0)
                {
                    lenses.Add(new CodeLens(new TextSpan(tests[0].Span.Start, 0), "▶ Run all tests in this file", RunTestsInFileCommand));
                    AddTestLenses(tests, lenses);
                }
                AddUseLenses(analysis, lenses);
                return lenses;
            }

            MarkupElement first = root.ChildElements.FirstOrDefault();
            if (first != null)
            {
                lenses.Add(new CodeLens(new TextSpan(first.Span.Start, 0), "▶ Run", RunCommand));
            }
            AddUseLenses(analysis, lenses);
            return lenses;
        }

        /// <summary>
        /// "3 uses" above each function of the file that something calls: the calls in this file, and whether another
        /// file of the project uses it too. A function nothing uses gets no lens; the unused warning covers it.
        /// </summary>
        private void AddUseLenses(DocumentAnalysis analysis, List<CodeLens> lenses)
        {
            ProjectUses project = null;
            foreach (Symbol fn in analysis.Symbols.Where(s => s.Kind == SymbolKind.Function && s.SourcePath == null && s.DeclaringElement != null))
            {
                int uses = analysis.ReferencesTo(fn).Count(r => !r.IsDeclaration);
                // Other files are only read for a function this file never calls, as the unused check does.
                bool elsewhere = uses == 0
                    && (project ??= ProjectUsage.Find(analysis.Path, analysis.Source.Text, Files, Catalog)).Names.Contains(fn.Name);
                string title = uses == 1 ? "1 use" : uses > 1 ? uses + " uses" : elsewhere ? "used in other files" : null;
                if (title != null)
                {
                    lenses.Add(new CodeLens(new TextSpan(fn.DeclaringElement.Span.Start, 0), title, ShowUsesCommand) { Target = fn.DeclarationSpan });
                }
            }
        }

        private static void AddTestLenses(List<TestItem> tests, List<CodeLens> lenses)
        {
            foreach (TestItem t in tests)
            {
                // A test whose name is filled in for each item is found by the part of its name before the first { }.
                string filter = t.IsTemplate && t.FullName.IndexOf('{') is int brace and >= 0 ? t.FullName.Substring(0, brace) : t.FullName;
                lenses.Add(new CodeLens(new TextSpan(t.Span.Start, 0), t.IsGroup ? "▶ Run tests" : "▶ Run test", RunTestCommand, filter));
                AddTestLenses(t.Children, lenses);
            }
        }
    }
}
