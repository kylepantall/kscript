using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis;

/// <summary>Known error types that can leave a call. Unresolved or dynamic calls carry no inferred contract.</summary>
internal sealed class FunctionErrors
{
    private readonly ILookup<MarkupElement, (Symbol Callee, TextSpan Span)> calls;
    private readonly Dictionary<string, string> parents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MarkupElement, ExprNode> thrown = new();
    private readonly HandlerRecovery recovery;
    private readonly Dictionary<MarkupElement, MarkupElement[]> typeHandlers = new();
    private readonly Dictionary<Symbol, string[]> documented = new();

    private FunctionErrors(DocumentAnalysis analysis, IReadOnlyDictionary<string, string> importedParents)
    {
        foreach (var parent in importedParents) parents[parent.Key] = parent.Value;
        recovery = new HandlerRecovery(analysis);
        foreach (var error in analysis.Markup.DescendantElements().Where(e => e.Name == "error"))
            if (error.GetAttributeValue("name") is string name) parents[name] = error.GetAttributeValue("extends") ?? ErrorTypes.Root;
        var types = analysis.DeclaredTypes.Keys.Where(e => e.GetAttributeValue("name") != null)
            .ToLookup(e => e.GetAttributeValue("name"), StringComparer.Ordinal);
        foreach (var owner in analysis.DeclaredTypes.Keys)
            typeHandlers[owner] = TypeHandlers(owner, types, new HashSet<MarkupElement>()).ToArray();
        var functionsByEnd = analysis.References.Where(r => !r.IsDeclaration && r.Symbol.Kind == SymbolKind.Function)
            .ToLookup(r => r.Span.End);
        var resolved = new List<(MarkupElement Element, Symbol Callee, TextSpan Span)>();
        foreach (var region in analysis.Regions)
        {
            if (region.Element == null) continue;
            if (region.Element.Name == "throw" && region.Attribute?.Name == "value") thrown[region.Element] = region.Parse?.Root;
            IEnumerable<ExprNode> expressions = region.Parse != null ? region.Parse.Nodes
                : region.Template?.Expressions.Select(segment => segment.Node) ?? Array.Empty<ExprNode>();
            foreach (var root in expressions)
                foreach (TextSpan call in InvocationNames(root))
                {
                    var span = region.Source.ToSource(call);
                    foreach (var reference in functionsByEnd[span.End])
                        resolved.Add((region.Element, reference.Symbol, reference.Span));
                }
        }
        foreach (var element in analysis.Markup.DescendantElements().Where(e => e.Name == "call"))
        {
            if (element.GetAttribute("fn")?.Value is not DecodedText target) continue;
            int end = target.ToSource(target.Value.TrimEnd().Length);
            foreach (var reference in functionsByEnd[end])
                resolved.Add((element, reference.Symbol, reference.Span));
        }
        calls = resolved.ToLookup(call => call.Element, call => (call.Callee, call.Span));
    }

    // Runtime type handlers try the receiver's own clauses first, then its bases left to right.
    private static IEnumerable<MarkupElement> TypeHandlers(MarkupElement owner, ILookup<string, MarkupElement> types, HashSet<MarkupElement> seen)
    {
        if (!seen.Add(owner)) yield break;
        foreach (var handler in owner.ChildElements.Where(e => e.Name == "catch")) yield return handler;
        foreach (string name in Analyzer.WithNames(owner))
            if (types[name].FirstOrDefault() is MarkupElement parent)
                foreach (var handler in TypeHandlers(parent, types, seen)) yield return handler;
    }

    private static IEnumerable<TextSpan> InvocationNames(ExprNode node)
    {
        if (node == null || node is LambdaNode) yield break;
        switch (node)
        {
            case CommandCallNode { ArgumentList: not null } command: yield return command.NameSpan; break;
            case MethodCallNode method: yield return method.NameSpan; break;
            case CallNode call: yield return call.Callee.Span; break;
        }
        foreach (var child in node.Children)
            foreach (var span in InvocationNames(child)) yield return span;
    }

    internal static void Infer(DocumentAnalysis analysis, IReadOnlyDictionary<string, string> importedParents)
    {
        var functions = analysis.Symbols.Where(s => s.Kind == SymbolKind.Function && s.SourcePath == null
            && s.DeclaringElement?.Name == "fn").ToArray();
        if (!analysis.Markup.DescendantElements().Any(e => e.Name == "throw") &&
            !functions.Any(fn => fn.Description?.Contains("@throws", StringComparison.Ordinal) == true) &&
            !analysis.References.Any(r => r.Symbol.EscapingErrors.Count > 0)) return;
        var inference = new FunctionErrors(analysis, importedParents);
        foreach (var fn in functions)
            inference.documented[fn] = fn.Doc?.Throws.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();

        // The sets only grow. A cycle of functions reaches the same result whichever one was declared first.
        bool changed;
        do
        {
            changed = false;
            foreach (var fn in functions)
            {
                var element = fn.DeclaringElement;
                var errors = inference.Visit(element, Array.Empty<string>(), null);
                if (element.Parent != null && inference.typeHandlers.TryGetValue(element.Parent, out var handlers))
                    inference.Handle(errors, handlers, declaration: true);
                errors.UnionWith(inference.documented[fn]);
                if (errors.Count == fn.EscapingErrors.Count) continue;
                fn.EscapingErrors = errors.OrderBy(name => name, StringComparer.Ordinal).ToArray();
                changed = true;
            }
        } while (changed);
        if (analysis.Kind != DocumentKind.Script) return;
        foreach (var root in analysis.Markup.RootElements.Where(e => e.Name == "kscript"))
            foreach (var statement in root.ChildElements.Where(e => !Deferred(e)))
                inference.CheckBoundary(analysis, statement);
    }

    // These bodies are invoked later by the test runner, HTTP server or runtime, outside script entry.
    private static bool Deferred(MarkupElement element) => element.Name is "fn" or "type" or "model" or "enum" or "error" or "extend" or "job"
        or "test" or "describe" or "before_each" or "after_each" or "Routes" or "KScript.Routes" or "Migration" or "on";

    private void CheckBoundary(DocumentAnalysis analysis, MarkupElement statement)
    {
        var escaping = Visit(statement, Array.Empty<string>(), null);
        if (escaping.Count == 0) return;
        // An invalid call or throw already has a more precise diagnostic; its effects aren't established.
        if (analysis.Diagnostics.Any(d => (d.Severity == DiagnosticSeverity.Error || d.Code == DiagnosticCodes.UnknownErrorType)
            && statement.Span.ContainsStrict(d.Span.Start))) return;
        var site = calls[statement].Where(call => call.Callee.EscapingErrors.Any(escaping.Contains)).Take(2).ToArray();
        var span = site.Length == 1 ? site[0].Span : statement.NameSpan;
        if (span.Length == 0) span = statement.Span;
        string names = string.Join(", ", escaping.OrderBy(name => name, StringComparer.Ordinal));
        analysis.Diagnostics.Add(new Diagnostic(span, DiagnosticSeverity.Warning, DiagnosticCodes.UnhandledError,
            $"This statement can let {names} escape the script. Handle the error with a matching catch."));
    }

    private HashSet<string> Visit(MarkupElement element, IReadOnlyCollection<string> caught, string binding)
    {
        var errors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var call in calls[element]) errors.UnionWith(call.Callee.EscapingErrors);
        if (element.Name == "expect_error" || element.Name == "expect" && element.GetAttribute("throws") != null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (element.Name == "throw")
        {
            string type = element.GetAttributeValue("type");
            var value = thrown.GetValueOrDefault(element);
            if (type != null) errors.Add(type.Trim());
            else if (value is MapNode map && map.Entries.LastOrDefault(e => e.Key == "type")?.Value is LiteralNode { Kind: LiteralKind.String, Template: null } literal)
                errors.Add(literal.Value);
            else if (value is CallNode { Callee: IdentifierNode id } && (parents.ContainsKey(id.Name) || ErrorTypes.BuiltIn.ContainsKey(id.Name)))
                errors.Add(id.Name);
            else if (caught.Count > 0 && (value is VariableNode variable && variable.Name == binding ||
                value == null && !element.Children.OfType<MarkupText>().Any(t => !t.IsWhitespace)))
                errors.UnionWith(caught);
            else errors.Add(ErrorTypes.Root);
        }
        foreach (var child in element.ChildElements)
        {
            if (Deferred(child) || child.Name is "catch" or "finally") continue;
            errors.UnionWith(Visit(child, caught, binding));
        }
        if (element.Name is "try" or "fn")
        {
            Handle(errors, element.ChildElements.Where(c => c.Name == "catch"), declaration: element.Name == "fn");
            foreach (var cleanup in element.ChildElements.Where(c => c.Name == "finally")) errors.UnionWith(Visit(cleanup, caught, binding));
        }
        return errors;
    }

    private void Handle(HashSet<string> errors, IEnumerable<MarkupElement> handlers, bool declaration = false)
    {
        if (errors.Count == 0) return;
        var raised = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handler in handlers)
        {
            string filter = handler.GetAttributeValue("type");
            var types = filter == null ? null : CorePackage.ErrorNames(filter).ToArray();
            var matched = errors.Where(error => types == null || types.Any(t => Matches(error, t))).ToArray();
            if (matched.Length == 0) continue;
            if (handler.GetAttribute("when") == null) errors.ExceptWith(matched);
            string binding = handler.GetAttributeValue("as")?.Trim().TrimStart('$') ?? "err";
            raised.UnionWith(Visit(handler, matched, binding));
            if (declaration && recovery.MayDecline(handler)) raised.UnionWith(matched);
        }
        errors.UnionWith(raised);
    }

    private bool Matches(string name, string handler)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (name != null && seen.Add(name))
        {
            if (handler.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            if (ErrorTypes.BuiltIn.TryGetValue(name, out var type)) return type.IsA(handler);
            name = parents.GetValueOrDefault(name, ErrorTypes.Root);
        }
        return false;
    }
}
