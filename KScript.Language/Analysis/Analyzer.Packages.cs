using KScript.Projects;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Packages: which ones a script imports (KScript.Core always, the packages listed in kscript.json,
    /// <c>&lt;import package&gt;</c>, and the owners of <c>&lt;KScript.X&gt;</c> elements), and the
    /// "add an import" errors for elements, commands, methods and variables of a package that isn't imported.
    /// Pages don't import packages, so none of this applies to them.
    /// </summary>
    public sealed partial class Analyzer
    {
        private readonly HashSet<string> importedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool ChecksImports => Kind == DocumentKind.Script;

        /// <summary>The packages this script imports, for completion and hover.</summary>
        internal IReadOnlyCollection<string> ImportedPackages => importedPackages;

        private void ImportPackage(string package)
        {
            foreach (string p in catalog.WithRequired(package))
            {
                importedPackages.Add(p);
            }
        }

        private void ResolvePackages(Scope root)
        {
            ImportPackage("KScript.Core");
            if (!ChecksImports)
            {
                return;
            }

            // Test files (greet.test.ks.xml) get KScript.Test, as kscript test gives it to them.
            bool isTestFile = TestSettings.IsTestFileName(result.Path);
            if (isTestFile)
            {
                ImportPackage("KScript.Test");
            }

            foreach (string package in ProjectPackages(isTestFile))
            {
                string known = catalog.FindPackage(package);
                if (known != null) ImportPackage(known);
            }

            foreach (MarkupElement e in result.Markup.DescendantElements())
            {
                if (e.Name.StartsWith("KScript.", StringComparison.Ordinal))
                {
                    ElementInfo info = catalog.GetElement(e.Name.Substring(8));
                    if (info?.Package != null) ImportPackage(info.Package);
                }
                else if (e.Name == "import" && e.GetAttribute("package") is MarkupAttribute packageAttr && packageAttr.Value != null)
                {
                    HoistPackageImport(e, packageAttr, root);
                }
            }
            result.ImportedPackages = importedPackages.ToList();
        }

        private void HoistPackageImport(MarkupElement e, MarkupAttribute packageAttr, Scope root)
        {
            string name = packageAttr.ValueText.Trim();
            TextSpan span = TrimmedSpan(packageAttr.Value);
            string package = catalog.FindPackage(name);
            if (package == null)
            {
                IEnumerable<string> names = catalog.Packages.Keys.Where(p => p != "KScript.Core");
                string suggestion = Suggestions.Closest(name, names.Concat(names.Select(n => n.Substring(8))));
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownPackage,
                    $"There is no package called {name}." + (suggestion != null ? $" Did you mean {suggestion}?" : string.Empty) + $" Packages: {string.Join(", ", names)}.",
                    SuggestFix(span, name, names, n => n));
                return;
            }
            if (!catalog.Packages.TryGetValue(package, out _) || !catalog.Commands.Concat(catalog.Methods).Any(c => c.Package == package) && !catalog.Elements.Any(x => x.Package == package))
            {
                // Known by name (KScript.UI, KScript.AI) but not in this build of the tools.
                Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownPackage, $"{package} isn't installed in this copy of kscript.");
            }
            ImportPackage(package);

            MarkupAttribute asAttr = e.GetAttribute("as");
            if (string.IsNullOrWhiteSpace(asAttr?.ValueText))
            {
                return;
            }

            TextSpan aliasSpan = TrimmedSpan(asAttr.Value);
            Symbol alias = new Symbol
            {
                Name = asAttr.ValueText.Trim(),
                Kind = SymbolKind.Import,
                DeclarationSpan = aliasSpan,
                DeclaringElement = e,
                Description = $"The {package} package: {catalog.Packages[package]}",
                Shape = new TypeShape(KType.Map),
            };
            foreach (CallableInfo command in catalog.Commands.Where(c => c.Package == package))
            {
                alias.Shape.Fields.TryAdd(command.Name, new FieldInfo(command.Name, new TypeShape(KType.Fn), null, command.Description));
            }
            foreach (var variable in catalog.BuiltInVariables.Where(v => catalog.PackageOfVariable(v.Key) == package))
            {
                alias.Shape.Fields.TryAdd(variable.Key, new FieldInfo(variable.Key, new TypeShape(variable.Value.Type), null, variable.Value.Description));
            }
            result.Symbols.Add(alias);
            result.References.Add(new SymbolReference(alias, aliasSpan, true, true));
            root.Declare(alias, 0);
        }

        /// <summary>The packages listed in the nearest kscript.json (with test.packages for a test file).</summary>
        private IEnumerable<string> ProjectPackages(bool isTestFile)
        {
            for (string dir = directory; dir != null; dir = System.IO.Path.GetDirectoryName(dir))
            {
                string file = System.IO.Path.Combine(dir, ProjectConfig.FileName);
                if (!files.FileExists(file))
                {
                    continue;
                }
                string text = files.ReadText(file);
                if (text == null)
                {
                    return Enumerable.Empty<string>();
                }
                try
                {
                    ProjectConfig config = ProjectConfig.Parse(text, file);
                    return isTestFile ? config.Packages.Concat(config.Test.Packages).ToList() : config.Packages;
                }
                catch (ProjectConfigException)
                {
                    return Enumerable.Empty<string>();
                }
            }
            return Enumerable.Empty<string>();
        }

        /// <summary>Reports <paramref name="what"/> (such as "&lt;input&gt;") when its package isn't imported.</summary>
        private void RequirePackage(string package, TextSpan span, string what)
        {
            if (!ChecksImports || package == null || importedPackages.Contains(package))
            {
                return;
            }
            Report(span, DiagnosticSeverity.Error, DiagnosticCodes.MissingImport,
                $"{what} is part of {package}. Add <import package=\"{package}\" /> at the top of the script.",
                AddImportFix(package));
        }

        private QuickFix[] AddImportFix(string package)
        {
            MarkupElement root = result.Markup.RootElements.FirstOrDefault();
            if (root == null || !root.StartTagClosed || root.SelfClosing)
            {
                return null;
            }

            // After the last <import>, or straight after <kscript>, indented like the first child.
            MarkupElement lastImport = root.ChildElements.LastOrDefault(c => c.Name == "import");
            int at = lastImport?.Span.End ?? root.ContentSpan.Start;
            string indent = "  ";
            MarkupElement first = root.ChildElements.FirstOrDefault();
            if (first != null)
            {
                int lineStart = source.GetOffset(source.GetPosition(first.Span.Start).Line, 0);
                string lead = source.Text.Substring(lineStart, first.Span.Start - lineStart);
                if (lead.All(char.IsWhiteSpace)) indent = lead;
            }
            return new[] { new QuickFix($"Add <import package=\"{package}\" />", new TextChange(new TextSpan(at, 0), $"\n{indent}<import package=\"{package}\" />")) };
        }
    }
}
