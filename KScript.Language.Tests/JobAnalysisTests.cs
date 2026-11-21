using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of KScript.Queue: job declarations, the calls that make jobs, $job and completion.</summary>
    public class JobAnalysisTests
    {
        private sealed class FakeFiles : IFileSystem
        {
            private readonly Dictionary<string, string> files;
            public FakeFiles(Dictionary<string, string> files) => this.files = files.ToDictionary(k => Path.GetFullPath(k.Key), k => k.Value);
            public bool FileExists(string path) => files.ContainsKey(Path.GetFullPath(path));
            public bool DirectoryExists(string path) => false;
            public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string t) ? t : null;
            public IEnumerable<string> ListFiles(string directory, string pattern) => Enumerable.Empty<string>();
        }

        private static readonly LanguageService Service = new LanguageService(files: new FakeFiles(new Dictionary<string, string>
        {
            ["/tmp/jobs/jobs/remind.kscript"] = "job Remind(queue: \"mail\") {\n  user_id: int\n\n  fn handle() {\n    echo $user_id\n  }\n}\n",
        }));

        private const string DocPath = "/tmp/jobs/main.kscript";

        private const string Welcome = """
            /// Says welcome.
            job Welcome(queue: "mail", tries: 3, backoff: ['10s', '1m'], retry_on: [Network]) {
              user_id: int
              email: email
              name?: string

              fn handle() {
                echo "Hi {$name ?? 'there'} {$email} {$user_id} {$this.user_id} {$job.attempt} {$job.queue}"
              }

              fn failed(err) {
                echo $err.message
              }
            }

            """;

        private static DocumentAnalysis Open(string code) => WithoutUnused(Service.Analyze(code, DocPath));

        private static string Messages(string code) => string.Join("\n", Open(code).Diagnostics.Select(d => d.Code + " " + d.Message));

        [Fact]
        public void ValidJobCodeIsClean()
        {
            AssertClean(Open(Welcome + """
                dispatch Welcome(user_id: 1, email: 'a@example.com')
                Welcome(user_id: 2, email: 'b@example.com', name: 'Bo')->dispatch(delay: '5m', queue: 'later')
                job = Welcome(user_id: 3, email: 'c@example.com')
                $job->run()
                echo $job.email
                """));
        }

        [Fact]
        public void JobsFromOtherFilesCanBeMade()
        {
            AssertClean(Open("import \"jobs/remind.kscript\"\ndispatch Remind(user_id: 4)\n"));
            Assert.Contains("takes its fields by name", Messages("import \"jobs/remind.kscript\"\ndispatch Remind(4)\n"));
        }

        [Theory]
        [InlineData("dispatch Welcome(user_id: 1)", "Welcome() needs email")]
        [InlineData("dispatch Welcome(user_id: 1, email: 'a@example.com', mail: 'x')", "Welcome has no field 'mail'")]
        [InlineData("dispatch Welcome(1, 'a@example.com')", "takes its fields by name")]
        public void CallsThatMakeJobsAreChecked(string call, string message) =>
            Assert.Contains(message, Messages(Welcome + call + "\n"));

        [Theory]
        [InlineData("job lower {\n  fn handle() {\n  }\n}\n", "start with a capital letter, like types: Lower")]
        [InlineData("job Twice {\n  fn handle() {\n  }\n}\njob Twice {\n  fn handle() {\n  }\n}\n", "already declared")]
        [InlineData("job NoHandle {\n  a: int\n}\n", "needs a handle function")]
        [InlineData("job Extra {\n  fn handle() {\n  }\n  fn helper() {\n  }\n}\n", "not helper")]
        [InlineData("job Secret {\n  pw: password\n\n  fn handle() {\n  }\n}\n", "queued jobs are saved to disk")]
        [InlineData("job Dupe {\n  a: int\n  a: string\n\n  fn handle() {\n  }\n}\n", "listed twice")]
        public void BadJobDeclarationsAreReported(string code, string message) =>
            Assert.Contains(message, Messages(code));

        [Fact]
        public void XmlJobsAreCheckedToo()
        {
            const string path = "/tmp/jobs/main.ks.xml";
            DocumentAnalysis a = WithoutUnused(Service.Analyze("""
                <kscript>
                  <job name="Hello" fields="who: string, who: int">
                    <echo>not here</echo>
                    <fn name="handle"><echo>{ $who }</echo></fn>
                  </job>
                  <job name="Pw"><field name="secret" type="password" /><fn name="handle" /></job>
                </kscript>
                """, path));
            string messages = string.Join("\n", a.Diagnostics.Select(d => d.Message));
            Assert.Contains("listed twice", messages);
            Assert.Contains("can only hold <field> elements", messages);
            Assert.Contains("saved to disk", messages);

            Assert.DoesNotContain(WithoutUnused(Service.Analyze("<kscript><job name=\"1x\"><fn name=\"handle\" /></job></kscript>", path)).Diagnostics, d => d.Message.Contains("capital"));
        }

        [Fact]
        public void HoverShowsTheJobsFields()
        {
            string code = Welcome + "dispatch Wel‸come(user_id: 1, email: 'a@example.com')\n";
            (string text, int offset) = WithCaret(code);
            string hover = Service.GetHover(Service.Analyze(text, DocPath), offset).Markdown;
            Assert.Contains("Says welcome.", hover);
            Assert.Contains("job Welcome {\n  user_id: number", hover.Replace("int", "number"));
        }

        [Fact]
        public void JobVariablesComplete()
        {
            string code = Welcome.Replace("echo $err.message", "echo $job.‸");
            (string text, int offset) = WithCaret(code);
            List<string> labels = Service.GetCompletions(Service.Analyze(text, DocPath), offset).Select(i => i.Label).ToList();
            Assert.Contains("attempt", labels);
            Assert.Contains("dispatched_at", labels);
        }
    }
}
