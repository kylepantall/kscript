using KScript.Language.Agents;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Language.Outputs
{
    /// <summary>
    /// Something a project makes from its own code, such as the agent docs, that should follow the code as it changes.
    /// <c>kscript watch</c>, <c>kscript serve</c> and the editor (on save) rebuild every enabled output.
    /// </summary>
    public interface IProjectOutput
    {
        /// <summary>A short name for messages and <c>kscript watch --only</c>, such as "agents".</summary>
        string Name { get; }

        /// <summary>True when the project at <paramref name="root"/> wants this output kept up to date.</summary>
        bool IsEnabled(string root);

        /// <summary>
        /// Writes the output (or with <paramref name="check"/>, writes nothing) and returns the files that changed,
        /// relative to the root.
        /// </summary>
        IReadOnlyList<string> Write(string root, bool check);

        /// <summary>True for a file this output writes, relative to the root with / separators, so a watcher ignores it.</summary>
        bool Owns(string relative);
    }

    /// <summary>AGENTS.md and .kscript/agents (<see cref="AgentDocs"/>), kept up to date once .kscript/agents exists.</summary>
    public sealed class AgentsOutput : IProjectOutput
    {
        public string Name => "agents";

        public bool IsEnabled(string root) => AgentDocs.IsEnabled(root);

        public IReadOnlyList<string> Write(string root, bool check) => AgentDocs.Write(root, check);

        public bool Owns(string relative) => ProjectConfig.IsAgentFile(relative);
    }

    /// <summary>One output's result from <see cref="ProjectOutputs.Build"/>.</summary>
    public sealed record OutputResult(string Name, IReadOnlyList<string> Changed, string Error);

    /// <summary>
    /// The outputs every project can have, in the order they are built. Packages that make more (OpenAPI documents,
    /// the project's docs) add theirs with <see cref="Add"/> when kscript starts.
    /// </summary>
    public static class ProjectOutputs
    {
        private static readonly List<IProjectOutput> outputs = new List<IProjectOutput> { new AgentsOutput() };

        public static IReadOnlyList<IProjectOutput> All
        {
            get
            {
                lock (outputs) return outputs.ToList();
            }
        }

        /// <summary>Adds an output, replacing one with the same name.</summary>
        public static void Add(IProjectOutput output)
        {
            lock (outputs)
            {
                outputs.RemoveAll(o => o.Name == output.Name);
                outputs.Add(output);
            }
        }

        /// <summary>Removes an output by name (for tests).</summary>
        public static bool Remove(string name)
        {
            lock (outputs) return outputs.RemoveAll(o => o.Name == name) > 0;
        }

        /// <summary>The outputs the project at <paramref name="root"/> has turned on.</summary>
        public static IReadOnlyList<IProjectOutput> Enabled(string root) => All.Where(o => o.IsEnabled(root)).ToList();

        /// <summary>
        /// Builds every enabled output (or only <paramref name="only"/>) for the project at <paramref name="root"/>.
        /// A failing output is reported in its result and doesn't stop the others.
        /// </summary>
        public static IReadOnlyList<OutputResult> Build(string root, bool check = false, IEnumerable<IProjectOutput> only = null)
        {
            var results = new List<OutputResult>();
            foreach (IProjectOutput output in only ?? Enabled(root))
            {
                try
                {
                    results.Add(new OutputResult(output.Name, output.Write(root, check), null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectConfigException or KScriptException)
                {
                    results.Add(new OutputResult(output.Name, Array.Empty<string>(), ex.Message));
                }
            }
            return results;
        }

        private static readonly HashSet<string> IgnoredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules", "dist" };

        /// <summary>
        /// True when a change to <paramref name="relative"/> (relative to the root, / separators) can change an output:
        /// not a generated file, nor anything in a hidden, bin, obj, node_modules or dist folder.
        /// </summary>
        public static bool IsSource(string relative, IEnumerable<IProjectOutput> outputs = null)
        {
            if (string.IsNullOrEmpty(relative)) return false;
            relative = relative.Replace('\\', '/');
            string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith(".", StringComparison.Ordinal)) return false;
                if (i < parts.Length - 1 && IgnoredFolders.Contains(parts[i])) return false;
            }
            return !(outputs ?? All).Any(o => o.Owns(relative));
        }

        /// <summary>One line per output that changed, or failed, for a terminal or a log.</summary>
        public static IEnumerable<string> Describe(IReadOnlyList<OutputResult> results)
        {
            foreach (OutputResult result in results)
            {
                if (result.Error != null) yield return $"{result.Name}: {result.Error}";
                else if (result.Changed.Count > 0) yield return $"{result.Name}: updated {string.Join(", ", result.Changed)}";
            }
        }
    }
}
