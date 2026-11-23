using KScript.Runtime;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Rules
{
    // Rules can inspect plain strings and maps. Only expression nodes and pure methods listed here can run.
    internal sealed class RuleFunction
    {
        private static readonly PackageRegistry Packages = new PackageRegistry();
        private static readonly HashSet<string> Methods = new HashSet<string>(StringComparer.Ordinal)
        {
            "trim", "lower", "upper", "starts_with", "ends_with", "contains", "has", "count",
            "is_blank", "is_empty", "strip_prefix", "strip_suffix",
        };

        private static readonly MethodDefinition[] PureMethods = Packages.Core.Methods
            .Where(method => Methods.Contains(method.Name))
            .ToArray();

        private readonly LambdaNode lambda;

        private RuleFunction(LambdaNode lambda) => this.lambda = lambda;

        public static RuleFunction Parse(string text)
        {
            ParseResult parsed = ExpressionParser.ParseExpression(text);
            if (!parsed.Success || parsed.Root is not LambdaNode { Parameters.Count: 1 } function) return null;
            string parameter = function.Parameters[0].Name;
            ExprNode[] nodes = function.Body.DescendantsAndSelf().Take(129).ToArray();
            if (nodes.Length > 128 || nodes.Any(node => !Allowed(node, parameter))) return null;
            return new RuleFunction(function);
        }

        private static bool Allowed(ExprNode node, string parameter) => node switch
        {
            LiteralNode literal => literal.Kind is LiteralKind.String or LiteralKind.Number or LiteralKind.Bool or LiteralKind.Null,
            VariableNode variable => variable.Name == parameter,
            MethodCallNode method => Methods.Contains(method.Name),
            BinaryNode binary => binary.Operator is "==" or "!=" or "===" or "!==" or "<" or ">" or "<=" or ">=" or "and" or "or" or "??" or "+",
            UnaryNode unary => unary.Operator is "not" or "!",
            MemberNode or IndexNode or ConditionalNode or ArgumentNode => true,
            _ => false,
        };

        public object Evaluate(object input)
        {
            var interpreter = new Interpreter(new KScriptHost(TextWriter.Null), Packages, new InterpreterOptions { MaxSteps = 256 });
            var scope = new Scope();
            scope.Declare(lambda.Parameters[0].Name, input, isConst: true);
            // Use an empty module populated only with allowed methods. Loading a regular module would discover
            // project extensions and settings, which are outside the rule's expression context.
            var module = new Module(interpreter, null, new SourceText(string.Empty), null);
            foreach (MethodDefinition method in PureMethods) module.Methods.Add(method.Receiver + ":" + method.Name, method);
            return interpreter.Evaluate(lambda.Body, scope, new EvalContext(module, null));
        }

        public static Dictionary<string, object> Node(MarkupElement element, DocumentKind kind, SourceSyntax syntax)
        {
            var attributes = new Dictionary<string, object>(kind == DocumentKind.Script ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (MarkupAttribute attribute in element.Attributes) attributes.TryAdd(attribute.Name, attribute.ValueText);
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = element.Name,
                ["kind"] = kind == DocumentKind.Page ? "html" : syntax == SourceSyntax.Code ? "kscript" : "xml",
                ["attributes"] = attributes,
                ["has_body_content"] = element.Children.Any(child => child is not MarkupText text || !text.IsWhitespace),
            };
        }
    }
}
