using System.Reflection;

using KScript.Runtime;
using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Metadata;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>
    /// Keeps the interpreter and the language tools in step. When one of these fails after an interpreter change,
    /// the fix is almost always to add or correct a KScript.Metadata attribute on the class you touched.
    /// </summary>
    public class CatalogContractTests
    {
        private static readonly LanguageCatalog Catalog = LanguageCatalog.Default;

        public static IEnumerable<object[]> AllElements() => Catalog.Elements.Select(e => new object[] { e.Name });

        public static IEnumerable<object[]> AllCommands() => Catalog.Commands.Select(c => new object[] { c.Name });

        [Fact]
        public void EveryInstalledPackageIsInTheCatalog()
        {
            foreach (KScriptPackage package in PackageRegistry.Default.LoadAll())
            {
                Assert.True(Catalog.Packages.ContainsKey(package.Name), package.Name);
                foreach (ElementDefinition e in package.Elements.Values)
                {
                    Assert.True(Catalog.GetElement(e.Name)?.Package == e.Info.Package, $"<{e.Name}> from {package.Name} is in the catalog as part of {Catalog.GetElement(e.Name)?.Package ?? "nothing"}.");
                    Assert.True(e.Handler != null, $"<{e.Name}> in {package.Name} has no handler.");
                }
                foreach (CommandDefinition c in package.Commands.Values)
                {
                    Assert.True(Catalog.GetCommand(c.Name) != null, $"@{c.Name}() from {package.Name} is missing from the catalog.");
                }
                foreach (MethodDefinition m in package.Methods)
                {
                    Assert.Contains(Catalog.Methods, x => x.Name == m.Name && x.ReceiverType == m.Receiver);
                }
            }
        }

        [Fact]
        public void TheTerminalOnlyPackagesShipWithKScript()
        {
            foreach (string name in new[] { "KScript.Core", "KScript.Terminal", "KScript.Math", "KScript.IO", "KScript.Net", "KScript.Markdown", "KScript.Web" })
            {
                Assert.True(PackageRegistry.Default.Get(name) != null, $"{name} doesn't load.");
            }
        }

        [Fact]
        public void PackagesDontDefineTheSameElementOrCommand()
        {
            var elements = PackageRegistry.Default.LoadAll().SelectMany(p => p.Elements.Keys.Select(k => (Name: k, Package: p.Name))).GroupBy(x => x.Name).Where(g => g.Count() > 1).ToList();
            Assert.True(elements.Count == 0, "Elements in more than one package: " + string.Join(", ", elements.Select(g => $"<{g.Key}> ({string.Join(", ", g.Select(x => x.Package))})")));
            var commands = PackageRegistry.Default.LoadAll().SelectMany(p => p.Commands.Keys.Select(k => (Name: k, Package: p.Name))).GroupBy(x => x.Name).Where(g => g.Count() > 1).ToList();
            Assert.True(commands.Count == 0, "Commands in more than one package: " + string.Join(", ", commands.Select(g => $"@{g.Key} ({string.Join(", ", g.Select(x => x.Package))})")));
        }

        [Fact]
        public void PlannedItemsBelongToNoPackage()
        {
            Assert.True(Catalog.GetElement("render").Planned);
            Assert.Null(Catalog.GetElement("render").Package);
            Assert.Equal("KScript.AI", Catalog.GetElement("llm").Package);
            Assert.Equal("KScript.AI", Catalog.GetCommand("chat").Package);
            Assert.False(Catalog.GetElement("echo").Planned);
            Assert.Equal("KScript.Core", Catalog.GetElement("echo").Package);
            Assert.Equal("KScript.Terminal", Catalog.GetElement("input").Package);
            Assert.Equal("KScript.IO", Catalog.GetCommand("read_text").Package);
            Assert.Equal("KScript.Math", Catalog.GetCommand("sqrt").Package);
        }

        [Theory]
        [MemberData(nameof(AllElements))]
        public void ElementsAreDescribed(string name)
        {
            ElementInfo e = Catalog.GetElement(name);
            Assert.False(string.IsNullOrWhiteSpace(e.Description), $"<{name}> has no description.");
            Assert.True(e.Description.TrimEnd().EndsWith('.'), $"<{name}>'s description should be a sentence: {e.Description}");
            Assert.Null(Catalog.GetRetiredElement(name));
        }

        [Theory]
        [MemberData(nameof(AllElements))]
        public void AttributesAreWellFormed(string name)
        {
            ElementInfo e = Catalog.GetElement(name);
            Assert.Equal(e.Attributes.Count, e.Attributes.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (AttributeInfo a in e.Attributes)
            {
                Assert.False(string.IsNullOrWhiteSpace(a.Description), $"<{name} {a.Name}> has no description.");
                Assert.Matches("^[a-z][a-z0-9_-]*$", a.Name);
                if (a.Kind == AttributeKind.Enum)
                {
                    Assert.True(a.Options.Count > 0, $"<{name} {a.Name}> is an enum with no options.");
                }
                if (a.Kind == AttributeKind.Bool)
                {
                    Assert.True(a.Options.Count == 0 || a.Options.SequenceEqual(new[] { "yes", "no" }), $"<{name} {a.Name}> is a bool, so its options are yes/no.");
                }
            }
        }

        [Theory]
        [MemberData(nameof(AllElements))]
        public void PlacementRulesReferToKnownElements(string name)
        {
            ElementInfo e = Catalog.GetElement(name);
            foreach (string other in (e.MustFollow ?? Array.Empty<string>()).Concat(e.MustBeInside ?? Array.Empty<string>()))
            {
                Assert.True(Catalog.GetElement(other) != null, $"<{name}> refers to an unknown element <{other}>.");
            }
        }

        [Theory]
        [MemberData(nameof(AllElements))]
        public void ExamplesAreValidKScript2(string name)
        {
            ElementInfo e = Catalog.GetElement(name);
            if (e.Example == null)
            {
                return;
            }
            string path = e.Context == ElementContext.Page ? "example.html" : "example.ks.xml";
            string text = e.Context == ElementContext.Page ? e.Example : Script(ImportFor(e.Package) + e.Example);
            DocumentAnalysis a = Analyze(text, path);
            // Examples are fragments, so they may use variables and functions from around them. Everything else must be valid.
            string[] contextual = { DiagnosticCodes.UndefinedVariable, DiagnosticCodes.SetUndeclared, DiagnosticCodes.UndefinedFunction };
            List<Diagnostic> errors = a.Errors.Where(d => !contextual.Contains(d.Code)).ToList();
            Assert.True(errors.Count == 0, $"The example for <{name}> has errors:\n{Describe(a)}\n{e.Example}");
        }

        /// <summary>An import of the package, so examples and calls from it are checked as a script using it would be.</summary>
        private static string ImportFor(string package) =>
            package == null || package == "KScript.Core" ? string.Empty : $"<import package=\"{package}\" />\n";

        [Fact]
        public void LoopsAreMarked()
        {
            Assert.True(Catalog.GetElement("for").IsLoop);
            Assert.True(Catalog.GetElement("while").IsLoop);
            Assert.False(Catalog.GetElement("if").IsLoop);
        }

        [Fact]
        public void RetiredNamesPointAtRealReplacements()
        {
            foreach (string name in Catalog.RetiredElementNames)
            {
                RetiredInfo r = Catalog.GetRetiredElement(name);
                Assert.False(string.IsNullOrWhiteSpace(r.Replacement), $"<{name}> needs a replacement message.");
                Assert.Null(Catalog.GetElement(name));
                if (r.Rename != null)
                {
                    Assert.True(Catalog.GetElement(r.Rename) != null, $"<{name}> renames to unknown <{r.Rename}>.");
                }
            }
        }

        [Fact]
        public void RetiredAttributesRenameToRealAttributes()
        {
            foreach (var kv in Retired.Attributes)
            {
                Assert.False(string.IsNullOrWhiteSpace(kv.Value.Replacement), $"{kv.Key}= needs a replacement message.");
                if (kv.Value.Rename != null)
                {
                    Assert.True(Catalog.Elements.Any(e => e.GetAttribute(kv.Value.Rename) != null), $"{kv.Key}= renames to '{kv.Value.Rename}', which no element has.");
                }
            }
        }

        [Fact]
        public void RetiredCommandsAreGoneOrReplaced()
        {
            foreach (var kv in Retired.Commands)
            {
                Assert.False(string.IsNullOrWhiteSpace(kv.Value.Replacement), $"@{kv.Key} needs a replacement message.");
                if (Catalog.GetCommand(kv.Key) == null)
                {
                    Assert.NotNull(Catalog.GetRetiredCommand(kv.Key));
                }
                if (kv.Value.Rename != null)
                {
                    Assert.True(Catalog.GetCommand(kv.Value.Rename) != null, $"@{kv.Key} renames to unknown @{kv.Value.Rename}.");
                }
            }
        }

        [Theory]
        [MemberData(nameof(AllCommands))]
        public void CommandsAreDescribedAndCallable(string name)
        {
            CallableInfo c = Catalog.GetCommand(name);
            Assert.False(string.IsNullOrWhiteSpace(c.Description), $"@{name} has no description.");
            Assert.NotEmpty(c.Signatures);
            Assert.Null(Catalog.GetRetiredCommand(name));
            Assert.True(c.MinArgs <= c.MaxArgs);
            foreach (Signature s in c.Signatures)
            {
                Assert.Equal(s.Parameters.Count, s.Parameters.Select(p => p.Name).Distinct().Count());
                // Optional parameters come last.
                bool optional = false;
                foreach (KScript.Metadata.ParameterInfo p in s.Parameters)
                {
                    Assert.False(optional && !p.Optional && !p.Variadic, $"{c.Labels.First()}: required '{p.Name}' after an optional parameter.");
                    optional |= p.Optional;
                }
            }

            // A call with the minimum number of arguments must not be reported as wrong.
            string args = string.Join(", ", Enumerable.Repeat("'x'", c.MinArgs));
            DocumentAnalysis a = Analyze(Script(ImportFor(c.Package) + $"<echo>@{name}({args})</echo>"));
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.ArgumentCount || d.Code == DiagnosticCodes.UnknownCommand || d.Code == DiagnosticCodes.MissingImport);
        }

        [Fact]
        public void MethodsAreDescribedAndTyped()
        {
            foreach (CallableInfo m in Catalog.Methods)
            {
                Assert.False(string.IsNullOrWhiteSpace(m.Description), $"->{m.Name} has no description.");
                Assert.True(KScript.Metadata.NativeTypes.IsNative(m.ReceiverType) || typeof(KType).GetFields().Any(f => f.FieldType == typeof(string) && (string)f.GetValue(null) == m.ReceiverType), $"->{m.Name} has an unknown receiver type '{m.ReceiverType}'.");
                Assert.True(KScript.Metadata.NativeTypes.IsNative(m.ReturnType) || typeof(KType).GetFields().Any(f => f.FieldType == typeof(string) && (string)f.GetValue(null) == m.ReturnType), $"->{m.Name} has an unknown return type '{m.ReturnType}'.");
            }
        }

        [Fact]
        public void MethodNamesAreUniquePerReceiverAndArity()
        {
            var clashes = Catalog.Methods
                .SelectMany(m => m.Signatures.Select(s => (m.Name, m.ReceiverType, s.MinArgs, s.MaxArgs)))
                .GroupBy(x => x)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.ToString())
                .ToList();
            Assert.True(clashes.Count == 0, "Duplicate method signatures: " + string.Join(", ", clashes));
        }

        [Fact]
        public void KeywordsAreNotUsableAsNames()
        {
            foreach (string keyword in Lexer.Keywords)
            {
                Assert.Null(Catalog.GetCommand(keyword));
                AssertSingle(Analyze(Script($"<let name=\"{keyword}\" value=\"1\" />")), DiagnosticCodes.InvalidAttributeValue);
            }
        }

        [Fact]
        public void DiagnosticCodesAreUniqueAndGrouped()
        {
            var codes = typeof(DiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(string))
                .Select(f => (Name: f.Name, Code: (string)f.GetValue(null)))
                .ToList();
            Assert.Equal(codes.Count, codes.Select(c => c.Code).Distinct().Count());
            Assert.All(codes, c => Assert.Matches("^KS[1-6][0-9]{3}$", c.Code));
        }

        [Fact]
        public void BuiltInVariablesAreDescribed()
        {
            foreach (var v in Catalog.BuiltInVariables)
            {
                Assert.False(string.IsNullOrWhiteSpace(v.Value.Description), "$" + v.Key);
            }
            Assert.NotEmpty(Catalog.FieldsOf(KType.Request));
        }
    }
}
