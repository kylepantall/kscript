using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>One-of parameter groups (<c>add|remove: file</c>): a caller passes exactly one member, by name.</summary>
    public class OneOfParameterTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private const string Board = """
            fn file(note_id: int, add|remove: string, notify: bool = true) {
              echo "{ $note_id } { $add ?? '' } { $remove ?? '' } { $notify }"
            }

            fn send(to: string, (text: string | template: int), at|after?: string) {
              echo "{ $to } { $text ?? '' } { $template ?? 0 } { $at ?? '' } { $after ?? '' }"
            }

            """;

        private static DocumentAnalysis Check(string text) => WithoutUnused(Service.Analyze(text, "/tmp/one-of/main.kscript"));

        private static string Messages(string text) => string.Join("\n", Check(text).Diagnostics.Select(d => d.Code + " " + d.Message));

        [Theory]
        [InlineData("@file(1, add: 'a.png')")]
        [InlineData("@file(1, remove: 'a.png', false)")]
        [InlineData("@file(remove: 'a.png', note_id: 2)")]
        [InlineData("@file(1, add: 'a.png', remove: null)")]
        [InlineData("@send('ada', text: 'hi')")]
        [InlineData("@send('ada', template: 3, after: '5m')")]
        public void OneMemberOfEachGroupIsFine(string call) => AssertClean(Check(Board + call + "\n"));

        [Theory]
        [InlineData("@file(1, add: 'a', remove: 'b')", "KS5008 @file() takes add: or remove:, not both.")]
        [InlineData("@file(1)", "KS5004 @file() needs add: or remove:.")]
        [InlineData("@send('ada', 'hi')", "KS5008 @send() takes 'text' by name, as one of a group: write text: and the value.")]
        [InlineData("@send('ada', text: 'a', at: 'now', after: '1m')", "KS5008 @send() takes at: or after:, not both.")]
        [InlineData("@send('ada', at: 'now')", "KS5004 @send() needs text: or template:.")]
        public void TwoMembersOrNoneOfARequiredGroupIsAnError(string call, string message) =>
            Assert.Equal(message, Messages(Board + call + "\n"));

        [Fact]
        public void TheSecondMemberCanBeRemoved()
        {
            string text = Board + "@file(1, add: 'a', remove: 'b')\n";
            Diagnostic d = Assert.Single(Check(text).Diagnostics);
            Assert.Equal("remove", text.Substring(d.Span.Start, d.Span.Length));
            QuickFix fix = Assert.Single(d.Fixes);
            Assert.Equal("Remove remove", fix.Title);
            TextChange change = Assert.Single(fix.Changes);
            Assert.EndsWith("@file(1, add: 'a')\n", text.Substring(0, change.Span.Start) + change.NewText + text.Substring(change.Span.End));
        }

        [Theory]
        [InlineData("fn f(to: string, text: string | template: int) {\n  echo 1\n}\n", "Put brackets around a group whose members have different types when other parameters sit beside it: (text: string | template: int).")]
        [InlineData("fn f(add|remove: string = 'x') {\n  echo 1\n}\n", "A member of a one-of group can't have a default")]
        [InlineData("fn f(at?|after: string) {\n  echo 1\n}\n", "Put the ? once, after the last name: at|after?: date.")]
        [InlineData("fn f((at?: date | after: duration)) {\n  echo 1\n}\n", "Put the ? after the brackets")]
        [InlineData("fn f((add: string)) {\n  echo 1\n}\n", "A one-of group needs two or more names")]
        public void BadGroupsAreReported(string declaration, string message) =>
            Assert.Contains(message, Messages(declaration));

        [Fact]
        public void AMixedGroupOnItsOwnNeedsNoBrackets() =>
            AssertClean((Check("fn f(text: string | template: int) {\n  echo \"{ $text } { $template }\"\n}\n@f(template: 2)\n")));

        [Fact]
        public void HoverAndSignatureHelpShowTheGroupAsWritten()
        {
            (string text, int offset) = WithCaret(Board + "@file(1, add: 'a', ‸)\n");
            DocumentAnalysis analysis = Check(text);
            SignatureHelpResult help = Service.GetSignatureHelp(analysis, offset);
            SignatureInfo signature = Assert.Single(help.Signatures);
            Assert.Equal("@file(note_id: int, add|remove: string, notify: bool = true)", signature.Label);
            // The next positional argument is notify: the group's members are only ever named.
            Assert.Equal(3, help.ActiveParameter);
            Assert.Equal("add|remove: string", signature.Highlights[1]);
            Assert.Equal("add|remove: string", signature.Highlights[2]);

            (text, offset) = WithCaret(Board + "@se‸nd('ada', text: 'hi')\n");
            var hover = Service.GetHover(Check(text), offset);
            Assert.Contains("@send(to: string, (text: string | template: int), at|after?: string)", hover.Markdown);
        }

        [Fact]
        public void CompletionDropsTheOtherMembersOnceOneIsWritten()
        {
            (string text, int offset) = WithCaret(Board + "@file(1, ‸)\n");
            List<string> labels = Service.GetCompletions(Check(text), offset).Select(c => c.Label).ToList();
            Assert.Contains("add:", labels);
            Assert.Contains("remove:", labels);
            Assert.Contains("notify:", labels);
            CompletionItem add = Service.GetCompletions(Check(text), offset).Single(c => c.Label == "add:");
            Assert.Equal("add|remove: string", add.Detail);
            Assert.Contains("One of add: or remove: pass only one.", add.Documentation);

            (text, offset) = WithCaret(Board + "@file(1, add: 'a', ‸)\n");
            labels = Service.GetCompletions(Check(text), offset).Select(c => c.Label).ToList();
            Assert.DoesNotContain("remove:", labels);
            Assert.DoesNotContain("add:", labels);
            Assert.Contains("notify:", labels);
        }

        [Fact]
        public void TheGroupsPartsShareOneColour()
        {
            string text = Board + "@file(1, add: 'a')\n";
            DocumentAnalysis analysis = Check(text);
            List<string> parts = Service.GetSemanticTokens(analysis).Where(t => t.Modifiers.HasFlag(SemanticTokenModifiers.OneOf))
                .Select(t => text.Substring(t.Span.Start, t.Span.Length)).ToList();
            // file: add | remove; send: text | template, at | after; the call: add.
            Assert.Equal(new[] { "add", "|", "remove", "text", "|", "template", "at", "|", "after", "add" }, parts);
        }

        [Fact]
        public void TheSharedReaderKeepsEachGroupTogether()
        {
            var problems = new List<ParameterProblem>();
            List<ParameterEntry> entries = ParameterList.Read("note_id: int, add|remove: file, (at: date | after: duration)?, mode: a|b", problems);
            Assert.Empty(problems);
            Assert.Equal(new[] { "note_id", "add", "remove", "at", "after", "mode" }, entries.Select(e => e.Name));
            Assert.Equal(new[] { -1, 0, 0, 1, 1, -1 }, entries.Select(e => e.Group));
            Assert.Equal(new[] { false, false, false, true, true, false }, entries.Select(e => e.GroupOptional));
            Assert.Equal("a|b", entries[5].Type);
            Assert.Equal("add: or remove:", ParameterList.Choices(new[] { "add", "remove" }));
            Assert.Equal("post:, put: or patch:", ParameterList.Choices(new[] { "post", "put", "patch" }));
            Assert.Equal("(at: date | after: duration)?", ParameterList.GroupLabel(new[] { "at", "after" }, new[] { "date", "duration" }, true));
            Assert.Equal("add|remove?: file", ParameterList.GroupLabel(new[] { "add", "remove" }, new[] { "file", "file" }, true));
        }
    }
}
