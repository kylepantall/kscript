using KScript.Language.Agents;
using KScript.Language.Outputs;

namespace KScript.LanguageServer
{
    /// <summary>
    /// <c>kscript watch [folder] [--once] [--check]</c>: builds everything the project makes from its code (the agent
    /// docs, and the outputs other packages add to <see cref="ProjectOutputs"/>), then rebuilds it whenever a file
    /// changes, until Ctrl+C. <c>--once</c> builds and stops; <c>--check</c> writes nothing and fails when anything is
    /// out of date, for CI.
    /// </summary>
    public static class WatchCommand
    {
        public const string Usage = "Usage: kscript watch [folder] [--once] [--check]";

        /// <summary>Returns 0 when done (or up to date), 1 when --check finds stale files or an output fails, 2 for bad usage.</summary>
        public static int Run(IEnumerable<string> args, TextWriter output, CancellationToken stop, TimeSpan? delay = null)
        {
            bool once = false, check = false;
            string folder = null;
            foreach (string arg in args)
            {
                switch (arg)
                {
                    case "--once":
                        once = true;
                        break;
                    case "--check":
                        check = true;
                        break;
                    case "--help":
                    case "-h":
                        output.WriteLine(Usage);
                        return 0;
                    default:
                        if (arg.StartsWith("-", StringComparison.Ordinal) || folder != null)
                        {
                            output.WriteLine(Usage);
                            return 2;
                        }
                        folder = arg;
                        break;
                }
            }

            folder ??= KScript.Runtime.WorkingFolder.Current;
            if (!Directory.Exists(folder))
            {
                output.WriteLine($"{folder}: folder not found");
                return 2;
            }

            string root = AgentDocs.FindRoot(folder);

            IReadOnlyList<IProjectOutput> enabled = ProjectOutputs.Enabled(root);
            if (enabled.Count == 0)
            {
                // A kscript.json that can't be read is the problem to report, not "nothing to build".
                try
                {
                    KScript.Projects.ProjectConfig.FindAndLoad(root);
                }
                catch (KScript.Projects.ProjectConfigException ex)
                {
                    KScript.Runtime.ErrorReport.Write(output, ex);
                    return 2;
                }
                output.WriteLine($"Nothing to build in {root}. Run kscript agents to start the agent docs, which this then keeps up to date.");
                return check ? 0 : 2;
            }

            IReadOnlyList<OutputResult> results = ProjectOutputs.Build(root, check);
            bool failed = results.Any(r => r.Error != null);
            if (check)
            {
                foreach (OutputResult result in results)
                {
                    if (result.Error != null) output.WriteLine($"{result.Name}: {result.Error}");
                    foreach (string file in result.Changed) output.WriteLine($"{file}: out of date ({result.Name})");
                }
                if (results.All(r => r.Error == null && r.Changed.Count == 0))
                {
                    output.WriteLine($"Up to date: {string.Join(", ", enabled.Select(o => o.Name))}.");
                    return 0;
                }
                output.WriteLine("Run kscript watch --once to update them.");
                return 1;
            }

            foreach (string line in ProjectOutputs.Describe(results)) output.WriteLine(line);
            if (once)
            {
                if (!failed) output.WriteLine($"Up to date: {string.Join(", ", enabled.Select(o => o.Name))}.");
                return failed ? 1 : 0;
            }

            output.WriteLine($"Watching {root} for {string.Join(", ", enabled.Select(o => o.Name))}. Press Ctrl+C to stop.");
            var watcher = new ProjectWatcher(root, null, line => output.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"))
            {
                Delay = delay ?? TimeSpan.FromMilliseconds(300),
            };
            watcher.RunAsync(stop).GetAwaiter().GetResult();
            return 0;
        }
    }
}
