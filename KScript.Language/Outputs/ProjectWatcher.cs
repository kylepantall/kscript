using KScript.Projects;

namespace KScript.Language.Outputs
{
    /// <summary>
    /// Watches a project folder and rebuilds its outputs (<see cref="ProjectOutputs"/>) a moment after its files stop
    /// changing, so edits made anywhere (an editor without the extension, a coding agent, git) keep them up to date.
    /// Changes to the outputs themselves, hidden folders, bin, obj, node_modules and dist are ignored.
    /// </summary>
    public sealed class ProjectWatcher
    {
        private readonly string root;
        private readonly IReadOnlyList<IProjectOutput> outputs;
        private readonly Action<string> log;
        private readonly object gate = new object();
        private DateTime due = DateTime.MaxValue;

        /// <param name="root">The project folder.</param>
        /// <param name="outputs">What to rebuild; null for every output the project has enabled.</param>
        /// <param name="log">Gets one line per output that changed or failed.</param>
        public ProjectWatcher(string root, IReadOnlyList<IProjectOutput> outputs, Action<string> log)
        {
            this.root = Path.GetFullPath(root);
            this.outputs = outputs;
            this.log = log;
        }

        /// <summary>How long the files must be quiet before a rebuild.</summary>
        public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(300);

        /// <summary>How many rebuilds have run (for tests).</summary>
        public int Builds { get; private set; }

        /// <summary>Records that <paramref name="path"/> changed; a rebuild follows after <see cref="Delay"/>. Returns false for paths it ignores.</summary>
        public bool Changed(string path)
        {
            string full = Path.GetFullPath(path);
            string relative = Path.GetRelativePath(root, full);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return false;
            if (!ProjectOutputs.IsSource(relative, outputs ?? ProjectOutputs.All)) return false;
            lock (gate) due = DateTime.UtcNow + Delay;
            return true;
        }

        /// <summary>Watches until <paramref name="stop"/> is cancelled.</summary>
        public async Task RunAsync(CancellationToken stop)
        {
            using var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, e) => Changed(e.FullPath);
            watcher.Created += (_, e) => Changed(e.FullPath);
            watcher.Deleted += (_, e) => Changed(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                Changed(e.OldFullPath);
                Changed(e.FullPath);
            };
            watcher.EnableRaisingEvents = true;

            TimeSpan tick = Delay < TimeSpan.FromMilliseconds(100) ? Delay : TimeSpan.FromMilliseconds(100);
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(tick, stop);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                BuildIfDue();
            }
        }

        /// <summary>Rebuilds when a change is due; returns true when it did.</summary>
        public bool BuildIfDue()
        {
            lock (gate)
            {
                if (DateTime.UtcNow < due) return false;
                due = DateTime.MaxValue;
            }
            Build();
            return true;
        }

        /// <summary>Rebuilds now and logs what changed.</summary>
        public IReadOnlyList<OutputResult> Build()
        {
            IReadOnlyList<OutputResult> results = ProjectOutputs.Build(root, only: outputs);
            Builds++;
            foreach (string line in ProjectOutputs.Describe(results)) log(line);
            return results;
        }
    }
}
