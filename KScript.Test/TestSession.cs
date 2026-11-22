using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Testing
{
    /// <summary>A &lt;describe&gt; group, or the file itself (the root group, with no name).</summary>
    public sealed class TestGroup
    {
        internal TestGroup(string name, TestGroup parent)
        {
            Name = name;
            Parent = parent;
        }

        public string Name { get; }
        public TestGroup Parent { get; }

        /// <summary>Why the group's tests are skipped (empty for no reason given), or null when they run.</summary>
        public string Skip { get; internal set; }

        public bool Only { get; internal set; }

        internal List<TestHook> BeforeEach { get; } = new List<TestHook>();
        internal List<TestHook> AfterEach { get; } = new List<TestHook>();

        /// <summary>This group and its parents, the file first.</summary>
        internal IEnumerable<TestGroup> Chain()
        {
            var chain = new List<TestGroup>();
            for (TestGroup g = this; g != null; g = g.Parent) chain.Insert(0, g);
            return chain;
        }
    }

    /// <summary>A &lt;before_each&gt; or &lt;after_each&gt; block.</summary>
    internal sealed class TestHook
    {
        public MarkupElement Element;
        public Module Module;
    }

    /// <summary>One test found in a test file: a &lt;test&gt;, or one item of a &lt;test each in&gt;.</summary>
    public sealed class TestCase
    {
        internal TestCase() { }

        public string Name { get; internal set; }

        /// <summary>The names of the &lt;describe&gt; groups around the test, outermost first.</summary>
        public IReadOnlyList<string> Groups { get; internal set; }

        /// <summary>The groups and the test name, joined with " › ".</summary>
        public string FullName => string.Join(" › ", Groups.Append(Name));

        /// <summary>Why the test is skipped (empty when no reason was given), or null when it runs.</summary>
        public string Skip { get; internal set; }

        public bool Only { get; internal set; }

        /// <summary>The test's own time limit in seconds, or null for the default.</summary>
        public double? Timeout { get; internal set; }

        public int Line { get; internal set; }

        internal MarkupElement Element { get; set; }
        internal Scope Scope { get; set; }
        internal Module Module { get; set; }
        internal TestGroup Group { get; set; }
    }

    /// <summary>
    /// The tests of one file. Running the file's top-level code (its imports, variables, functions and
    /// &lt;describe&gt; blocks) collects the tests; <see cref="TestRunner"/> then runs each one.
    /// </summary>
    public sealed class TestSession
    {
        internal const string Key = "KScript.Test.session";

        public TestSession()
        {
            Root = new TestGroup(null, null);
            Current = Root;
        }

        public TestGroup Root { get; }
        internal TestGroup Current { get; set; }
        public List<TestCase> Tests { get; } = new List<TestCase>();

        /// <summary>The test that is running, or null while tests are being collected.</summary>
        public TestCase Running { get; internal set; }

        /// <summary>The session of a run under kscript test, or null when the script was run some other way.</summary>
        public static TestSession Of(KScriptHost host) => host.Items.TryGetValue(Key, out object s) ? s as TestSession : null;

        public void Attach(KScriptHost host) => host.Items[Key] = this;
    }
}
