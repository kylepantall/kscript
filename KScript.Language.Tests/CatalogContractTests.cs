using System.Reflection;

using KScript.Handlers;
using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Language.Syntax;
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
        public void EveryElementClassIsClassified()
        {
            Assert.True(Catalog.UnclassifiedElementTypes.Count == 0,
                "Add [KScriptElement] (with [KScriptDoc]) or [KScriptRetired] to: " + string.Join(", ", Catalog.UnclassifiedElementTypes.Select(t => t.FullName)));
        }

        [Fact]
        public void EveryCommandClassIsClassified()
        {
            Assert.True(Catalog.UnclassifiedCommandTypes.Count == 0,
                "Add [KScriptDoc] or [KScriptRetired] to: " + string.Join(", ", Catalog.UnclassifiedCommandTypes.Select(t => t.FullName)));
        }

        [Fact]
        public void ElementClassesAreNotBothCurrentAndRetired()
        {
            foreach (Type t in LanguageCatalog.InterpreterElementTypes().Concat(LanguageCatalog.InterpreterCommandTypes()))
            {
                bool current = t.GetCustomAttribute<KScriptElementAttribute>() != null;
                bool retired = t.GetCustomAttribute<KScriptRetiredAttribute>() != null;
                Assert.False(current && retired, t.FullName + " is marked both current and retired.");
            }
        }

        [Fact]
        public void CurrentElementClassesHaveDocs()
        {
            foreach (Type t in LanguageCatalog.InterpreterElementTypes().Where(t => t.GetCustomAttribute<KScriptElementAttribute>() != null))
            {
                Assert.True(t.GetCustomAttribute<KScriptDocAttribute>() != null, t.FullName + " needs [KScriptDoc].");
            }
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
                if (a.Kind == KScriptValueKind.Enum)
                {
                    Assert.True(a.Options.Count > 0, $"<{name} {a.Name}> is an enum with no options.");
                }
                if (a.Kind == KScriptValueKind.Bool)
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
            string path = e.Context == KScriptElementContext.Page ? "example.html" : "example.ks.xml";
            string text = e.Context == KScriptElementContext.Page ? e.Example : Script(e.Example);
            DocumentAnalysis a = Analyze(text, path);
            // Examples are fragments, so they may use variables and functions from around them. Everything else must be valid.
            string[] contextual = { DiagnosticCodes.UndefinedVariable, DiagnosticCodes.SetUndeclared, DiagnosticCodes.UndefinedFunction };
            List<Diagnostic> errors = a.Errors.Where(d => !contextual.Contains(d.Code)).ToList();
            Assert.True(errors.Count == 0, $"The example for <{name}> has errors:\n{Describe(a)}\n{e.Example}");
        }

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
            foreach (Type t in LanguageCatalog.InterpreterElementTypes())
            {
                foreach (PropertyInfo p in t.GetProperties())
                {
                    KScriptRetiredAttribute retired = p.GetCustomAttribute<KScriptRetiredAttribute>();
                    if (retired?.Rename == null || t.GetCustomAttribute<KScriptElementAttribute>() == null)
                    {
                        continue;
                    }
                    ElementInfo e = Catalog.GetElement(t.Name.TrimStart('@'));
                    Assert.True(e?.GetAttribute(retired.Rename) != null, $"{t.Name}.{p.Name} renames to '{retired.Rename}', which <{e?.Name}> doesn't have.");
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
                foreach (KScript.Language.Catalog.ParameterInfo p in s.Parameters)
                {
                    Assert.False(optional && !p.Optional && !p.Variadic, $"{c.Labels.First()}: required '{p.Name}' after an optional parameter.");
                    optional |= p.Optional;
                }
            }

            // A call with the minimum number of arguments must not be reported as wrong.
            string args = string.Join(", ", Enumerable.Repeat("'x'", c.MinArgs));
            DocumentAnalysis a = Analyze(Script($"<echo>@{name}({args})</echo>"));
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.ArgumentCount || d.Code == DiagnosticCodes.UnknownCommand);
        }

        [Fact]
        public void CommandParamDocsMatchConstructors()
        {
            foreach (Type t in LanguageCatalog.InterpreterCommandTypes())
            {
                HashSet<string> ctorParams = t.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.Name.TrimStart('@')).ToHashSet();
                foreach (KScriptParamAttribute p in t.GetCustomAttributes<KScriptParamAttribute>())
                {
                    Assert.True(ctorParams.Contains(p.Name), $"@{t.Name} documents a parameter '{p.Name}' that no constructor takes ({string.Join(", ", ctorParams)}).");
                }
            }
        }

        [Fact]
        public void ImplementedCommandsComeFromTheInterpreter()
        {
            foreach (Type t in LanguageCatalog.InterpreterCommandTypes().Where(t => t.GetCustomAttribute<KScriptRetiredAttribute>() == null))
            {
                CallableInfo c = Catalog.GetCommand(t.Name.TrimStart('@'));
                Assert.True(c != null, $"@{t.Name} is missing from the catalog.");
                Assert.Equal(t, c.ImplementedBy);
                Assert.False(c.Planned);
            }
            foreach (Type t in LanguageCatalog.InterpreterCommandTypes().Where(t => t.GetCustomAttribute<KScriptRetiredAttribute>() != null))
            {
                Assert.Null(Catalog.GetCommand(t.Name.TrimStart('@')));
                Assert.NotNull(Catalog.GetRetiredCommand(t.Name.TrimStart('@')));
            }
        }

        [Fact]
        public void EveryInterpreterValueMethodIsInTheCatalog()
        {
            foreach (var m in ValueMethods.Catalog())
            {
                Assert.False(string.IsNullOrWhiteSpace(m.Description), $"->{m.Name} has no description.");
                Assert.Contains(m.Category, new[] { "any", "string", "number", "bool" });
                CallableInfo info = Catalog.Methods.FirstOrDefault(c => c.Name == m.Name && c.ImplementedBy != null);
                Assert.True(info != null, $"->{m.Name} from ValueMethods is missing from the catalog.");
                Assert.True(info.MinArgs <= m.MinArgs && (info.MaxArgs >= m.MaxArgs || m.MaxArgs < 0),
                    $"->{m.Name} takes {m.MinArgs}..{m.MaxArgs} arguments in the interpreter but {info.MinArgs}..{info.MaxArgs} in the catalog.");
            }
        }

        [Fact]
        public void MethodsAreDescribedAndTyped()
        {
            foreach (CallableInfo m in Catalog.Methods)
            {
                Assert.False(string.IsNullOrWhiteSpace(m.Description), $"->{m.Name} has no description.");
                Assert.True(typeof(KType).GetFields().Any(f => f.FieldType == typeof(string) && (string)f.GetValue(null) == m.ReceiverType), $"->{m.Name} has an unknown receiver type '{m.ReceiverType}'.");
                Assert.True(typeof(KType).GetFields().Any(f => f.FieldType == typeof(string) && (string)f.GetValue(null) == m.ReturnType), $"->{m.Name} has an unknown return type '{m.ReturnType}'.");
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
            Assert.All(codes, c => Assert.Matches("^KS[1-5][0-9]{3}$", c.Code));
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
