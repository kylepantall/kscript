using KScript.Auth;
using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Projects;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// <c>policy for Post { }</c> in the checker: the block itself, the ability @can, @cannot and @authorize name,
    /// and an injection that takes a model with a policy without asking it. The rules and words are
    /// <see cref="PolicyRules"/>'s, so the editor and a running script agree.
    /// </summary>
    public sealed partial class Analyzer
    {
        private Dictionary<string, HashSet<string>> policies;

        /// <summary>The abilities of each model's policy: this file's, then the ones in the project's policies folder.</summary>
        private Dictionary<string, HashSet<string>> Policies()
        {
            if (policies != null) return policies;
            policies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            void Read(MarkupDocument doc)
            {
                foreach (MarkupElement e in doc.DescendantElements().Where(x => x.Name == "policy"))
                {
                    string model = e.GetAttributeValue("for")?.Trim();
                    if (string.IsNullOrEmpty(model) || policies.ContainsKey(model)) continue;
                    policies[model] = e.ChildElements.Where(c => c.Name == "fn").Select(c => c.GetAttributeValue("name")?.Trim())
                        .Where(n => !string.IsNullOrEmpty(n)).ToHashSet(StringComparer.Ordinal);
                }
            }
            Read(result.Markup);

            var pending = new Stack<string>();
            if (ProjectFiles((config, _, _, _, _) => new List<string> { PolicyRules.FolderOf(config) }).FirstOrDefault() is string root) pending.Push(root);
            while (pending.Count > 0)
            {
                string folder = pending.Pop();
                if (!files.DirectoryExists(folder)) continue;
                foreach (string sub in files.ListDirectories(folder)) pending.Push(sub);
                foreach (string path in files.ListFiles(folder, "*").Where(ScriptFiles.IsScript).OrderBy(p => p, StringComparer.Ordinal))
                {
                    if (files.ReadText(path) is string text) Read(MarkupParser.Parse(new SourceText(text), MarkupParser.KindFromPath(path), path));
                }
            }
            return policies;
        }

        /// <summary>Each policy block of the file: where it is, the model it names, and what it holds.</summary>
        private void CheckPolicies()
        {
            bool code = result.Markup.Syntax == SourceSyntax.Code;
            foreach (MarkupElement e in result.Markup.DescendantElements().Where(x => IsKScriptElementName(x, "policy") && catalog.GetElement("policy")?.Package == "KScript.Auth"))
            {
                if (e.Parent is MarkupElement parent && parent != result.Markup.RootElements.FirstOrDefault())
                {
                    Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement,
                        code ? "A policy goes at the top level of a file, not inside a block." : "<policy> must be a direct child of <kscript>.");
                }
                MarkupAttribute forAttr = e.GetAttribute("for");
                string model = forAttr?.ValueText?.Trim();
                if (string.IsNullOrEmpty(model)) continue;
                TextSpan span = TrimmedSpan(forAttr.Value);
                Symbol symbol = result.RootScope.Lookup(model, int.MaxValue);
                if (symbol?.Shape?.Type == KType.Type) result.References.Add(new SymbolReference(symbol, span, false, false));
                else Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, PolicyRules.NoModel(model));

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (MarkupElement child in e.ChildElements)
                {
                    if (!IsKScriptElementName(child, "fn"))
                    {
                        Report(child.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement,
                            code ? $"policy for {model} {{ }} holds fn abilities only, not {child.Name}." : $"<policy> holds <fn> elements only, not <{child.Name}>.");
                        continue;
                    }
                    MarkupAttribute nameAttr = child.GetAttribute("name");
                    string ability = nameAttr?.ValueText?.Trim();
                    if (string.IsNullOrEmpty(ability)) continue;
                    if (!seen.Add(ability))
                        Report(TrimmedSpan(nameAttr.Value), DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"The {model} policy has '{ability}' twice. Keep one.");
                    int count = child.GetAttribute("params")?.Value is DecodedText ps ? ParseParameters(ps, report: false).Count : 0;
                    if (PolicyRules.TooManyParameters(model, ability, count) is string tooMany)
                        Report(TrimmedSpan(nameAttr.Value), DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount, tooMany);
                }
            }
        }

        /// <summary>The model a value is a record of, or names: <c>$post</c> and <c>Post</c> are both Post.</summary>
        private string ModelNameOf(ExprNode value, ExpressionRegion region)
        {
            TypeShape shape = Infer(value, region);
            return shape.Type == KType.Type ? shape.Instance?.Name ?? shape.Name : shape.Name;
        }

        /// <summary>@can(update, $post): the ability must be one the model's policy has, when the policy can be read.</summary>
        private void CheckAbility(IReadOnlyList<ArgumentNode> arguments, ExpressionRegion region)
        {
            ArgumentNode ability = arguments.FirstOrDefault(a => a.Name == "ability") ?? arguments.FirstOrDefault(a => a.Name == null);
            ArgumentNode target = arguments.FirstOrDefault(a => a.Name == "target") ?? arguments.Where(a => a.Name == null).Skip(1).FirstOrDefault();
            string name = ability?.Value switch
            {
                IdentifierNode id => id.Name,
                LiteralNode { Kind: LiteralKind.String, Template: null } text => text.Value,
                _ => null,
            };
            if (name == null || target == null || ModelNameOf(target.Value, region) is not string model) return;
            if (!Policies().TryGetValue(model, out HashSet<string> has) || (has.Contains(name) && name != PolicyRules.Before)) return;
            Report(region.Source.ToSource(ability.Value.Span), DiagnosticSeverity.Error, DiagnosticCodes.InvalidArgumentValue, PolicyRules.NoAbility(model, name, has.OrderBy(a => a, StringComparer.Ordinal)));
        }

        /// <summary>KS5017: @inject() takes a record of a model that has a policy, and nothing asks the policy.</summary>
        private void CheckInjectAsks(string model, IReadOnlyList<ArgumentNode> arguments, TextSpan callSpan)
        {
            if (model == null || arguments.Any(a => a.Name == "can") || !Policies().ContainsKey(model)) return;
            Report(callSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UncheckedInjection, PolicyRules.Unchecked(model));
        }
    }
}
