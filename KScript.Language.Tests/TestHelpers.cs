using System.Text;

using KScript.Language.Analysis;
using KScript.Language.Syntax;

namespace KScript.Language.Tests
{
    internal static class TestHelpers
    {
        /// <summary>The cursor marker used in test documents.</summary>
        public const char Caret = '‸';

        /// <summary>Removes the caret and returns its offset.</summary>
        public static (string Text, int Offset) WithCaret(string text)
        {
            int offset = text.IndexOf(Caret);
            Assert.True(offset >= 0, "The test text needs a ‸ caret.");
            return (text.Remove(offset, 1), offset);
        }

        public static string Script(string body) => "<kscript>\n" + body + "\n</kscript>";

        public static DocumentAnalysis Analyze(string text, string path = "test.ks.xml", IFileSystem files = null) =>
            Analyzer.Analyze(text, path, files ?? EmptyFileSystem.Instance);

        public static string Describe(DocumentAnalysis a) =>
            string.Join("\n", a.Diagnostics.Select(d => $"{a.Source.GetPosition(d.Span.Start)} {d.Code} '{a.Source[d.Span]}': {d.Message}"));

        public static void AssertClean(DocumentAnalysis a) => Assert.True(a.Diagnostics.Count == 0, "Expected no diagnostics:\n" + Describe(a));

        public static Diagnostic AssertSingle(DocumentAnalysis a, string code)
        {
            List<Diagnostic> found = a.Diagnostics.Where(d => d.Code == code).ToList();
            Assert.True(found.Count == 1, $"Expected one {code}, got:\n" + Describe(a));
            return found[0];
        }

        /// <summary>Prints an expression tree as an S-expression, for compact precedence tests.</summary>
        public static string Print(ExprNode node) => node switch
        {
            null => "null",
            LiteralNode l => l.Kind == LiteralKind.String ? "'" + l.Value + "'" : l.Value,
            VariableNode v => "$" + v.Name,
            IdentifierNode i => i.Name,
            CommandCallNode c => "@" + c.Name + (c.ArgumentList == null ? string.Empty : "(" + string.Join(" ", c.Arguments.Select(Print)) + ")"),
            MethodCallNode m => "(-> " + Print(m.Target) + " " + m.Name + string.Concat(m.Arguments.Select(a => " " + Print(a))) + ")",
            CallNode call => "(call " + Print(call.Callee) + string.Concat(call.Arguments.Select(a => " " + Print(a))) + ")",
            ArgumentNode a => a.Name != null ? a.Name + ":" + Print(a.Value) : Print(a.Value),
            MemberNode m => "(" + (m.NullSafe ? "?." : ".") + " " + Print(m.Target) + " " + m.Name + ")",
            IndexNode ix => "([] " + Print(ix.Target) + " " + Print(ix.Index) + ")",
            UnaryNode u => "(" + u.Operator + " " + Print(u.Operand) + ")",
            BinaryNode b => "(" + b.Operator + " " + Print(b.Left) + " " + Print(b.Right) + ")",
            ConditionalNode c => "(? " + Print(c.Condition) + " " + Print(c.WhenTrue) + " " + Print(c.WhenFalse) + ")",
            PipeNode p => "(|> " + Print(p.Left) + " " + Print(p.Right) + ")",
            LambdaNode f => "(fn [" + string.Join(" ", f.Parameters.Select(p => p.Name)) + "] " + Print(f.Body) + ")",
            ListNode l => "[" + string.Join(" ", l.Items.Select(Print)) + "]",
            MapNode m => "{" + string.Join(" ", m.Entries.Select(e => e.Key + ":" + Print(e.Value))) + "}",
            RangeNode r => "(.. " + Print(r.Start) + " " + Print(r.End) + (r.Step != null ? " " + Print(r.Step) : string.Empty) + ")",
            MatchNode m => "(match " + Print(m.Subject) + string.Concat(m.Arms.Select(a => " " + (a.Pattern == null ? "_" : Print(a.Pattern)) + "=>" + Print(a.Result))) + ")",
            AssignmentNode a => "(" + a.Operator + " " + Print(a.Target) + " " + Print(a.Value) + ")",
            ErrorNode _ => "<error>",
            _ => node.GetType().Name,
        };
    }
}
