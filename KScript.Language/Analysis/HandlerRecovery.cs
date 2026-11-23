using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Analysis;

/// <summary>Whether a declaration catch can finish without producing a non-null fallback.</summary>
internal sealed class HandlerRecovery
{
    private readonly ILookup<MarkupElement, ExpressionRegion> returns;
    private readonly Dictionary<MarkupElement, bool> cache = new();

    private readonly HashSet<Symbol> nonNullParameters = new();

    internal HandlerRecovery(DocumentAnalysis analysis)
    {
        returns = analysis.Regions.Where(r => r.Element?.Name == "return" && r.Attribute?.Name == "value").ToLookup(r => r.Element);
        var parameters = analysis.Symbols.Where(s => s.Kind == SymbolKind.Parameter && s.DeclaringElement != null)
            .ToLookup(s => (s.DeclaringElement, s.Name));
        var written = analysis.References.Where(r => r.IsWrite && !r.IsDeclaration).Select(r => r.Symbol).ToHashSet();
        foreach (var fn in analysis.Symbols.Where(s => s.Kind == SymbolKind.Function && s.DeclaringElement != null))
            foreach (var parameter in fn.Parameters.Where(p => IsNonNullType(p.Type)))
                foreach (var symbol in parameters[(fn.DeclaringElement, parameter.Name)])
                    if (!written.Contains(symbol)) nonNullParameters.Add(symbol);
    }

    private static bool IsNonNullType(string type) =>
        TypeRef.Parse(type) is { Optional: false } parsed && (parsed.ListDepth > 0 || parsed.Name is not ("any" or "null"));

    internal bool MayDecline(MarkupElement clause)
    {
        if (cache.TryGetValue(clause, out bool result)) return result;
        string binding = clause.GetAttributeValue("as")?.Trim().TrimStart('$') ?? "err";
        return cache[clause] = Paths(clause.ChildElements, binding) != Exit.Handled;
    }

    internal bool MayDecline(CatchExpressionNode clause, ExpressionRegion region) =>
        !NonNull(clause.Fallback, clause.Binding, region);

    [Flags]
    private enum Exit { Handled = 0, Next = 1, Null = 2 }

    private Exit Paths(IEnumerable<MarkupElement> body, string binding)
    {
        var statements = body.ToArray();
        Exit paths = Exit.Next;
        for (int i = 0; i < statements.Length && paths.HasFlag(Exit.Next); i++)
        {
            var statement = statements[i];
            Exit next = Exit.Next;
            switch (statement.Name)
            {
                case "return":
                    var region = returns[statement].FirstOrDefault();
                    next = NonNull(region?.Parse?.Root, binding, region) ? Exit.Handled : Exit.Null;
                    break;
                case "throw": case "exit":
                    next = Exit.Handled;
                    break;
                case "if":
                    next = Paths(statement.ChildElements, binding);
                    while (i + 1 < statements.Length && statements[i + 1].Name == "elif")
                        next |= Paths(statements[++i].ChildElements, binding);
                    next |= i + 1 < statements.Length && statements[i + 1].Name == "else"
                        ? Paths(statements[++i].ChildElements, binding) : Exit.Next;
                    break;
                case "match":
                    next = statement.ChildElements.Any(c => c.Name == "default") ? Exit.Handled : Exit.Next;
                    foreach (var arm in statement.ChildElements) next |= Paths(arm.ChildElements, binding);
                    break;
                case "for": case "while":
                    // Zero iterations is possible, but a return inside the loop may still return null early.
                    next |= Paths(statement.ChildElements, binding) & Exit.Null;
                    break;
                case "try":
                    next = Paths(statement.ChildElements.Where(c => c.Name is not ("catch" or "finally")), binding);
                    foreach (var handler in statement.ChildElements.Where(c => c.Name == "catch"))
                        next |= Paths(handler.ChildElements, binding);
                    foreach (var cleanup in statement.ChildElements.Where(c => c.Name == "finally"))
                    {
                        Exit ending = Paths(cleanup.ChildElements, binding);
                        next = ending.HasFlag(Exit.Next) ? next | (ending & Exit.Null) : ending;
                    }
                    break;
            }
            paths = (paths & Exit.Null) | next;
        }
        return paths;
    }

    // An inferred result type alone does not prove that a value exists (for example, ->first() on a list).
    private bool NonNull(ExprNode value, string binding, ExpressionRegion region) => value switch
    {
        LiteralNode literal => literal.Kind != LiteralKind.Null,
        MapNode or ListNode or RangeNode or LambdaNode or UnaryNode => true,
        VariableNode v => v.Name == binding || v.Name == "this" || nonNullParameters.Contains(region.Scope.Lookup(v.Name, region.Source.ToSource(v.Span.Start))),
        MemberNode { Target: VariableNode v, Name: "message" or "type" } => v.Name == binding,
        ConditionalNode choice => NonNull(choice.WhenTrue, binding, region) && NonNull(choice.WhenFalse, binding, region),
        BinaryNode { Operator: "??" } coalesce => NonNull(coalesce.Left, binding, region) || NonNull(coalesce.Right, binding, region),
        BinaryNode => true,
        CastNode cast => IsNonNullType(cast.TypeName),
        CommandCallNode { ArgumentList: null } => true,
        MatchNode match => match.Arms.Any(a => a.Patterns.Count == 0 && a.Guard == null)
            && match.Arms.All(a => NonNull(a.Result, binding, region)),
        _ => false,
    };
}
