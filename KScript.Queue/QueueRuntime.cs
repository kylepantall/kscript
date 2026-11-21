using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using KScript.Projects;
using KScript.Runtime;

namespace KScript.Queueing
{
    /// <summary>A project's queue settings, as a script or the worker sees them: its kscript.json, its app name and its environment.</summary>
    public sealed class QueueContext
    {
        public QueueContext(ProjectConfig project, string directory, string environment, bool testing = false)
        {
            Project = project;
            Directory = project?.Directory ?? directory;
            Environment = environment;
            Testing = testing;
        }

        public ProjectConfig Project { get; }

        /// <summary>The project folder (or the script's folder outside a project).</summary>
        public string Directory { get; }

        public string Environment { get; }

        /// <summary>True under kscript test, where jobs run as they are dispatched unless queue.env.test says otherwise.</summary>
        public bool Testing { get; }

        public QueueSettings Settings => Project?.Queue ?? DefaultSettings;

        private static readonly QueueSettings DefaultSettings = new QueueSettings();

        public string AppName => Project?.AppName ?? AppFolders.CleanName(Path.GetFileName(Directory?.TrimEnd(Path.DirectorySeparatorChar)) ?? "kscript");

        /// <summary>The connection jobs go to unless they name one.</summary>
        public string DefaultConnection
        {
            get
            {
                if (Testing && !Settings.DefaultByEnvironment.ContainsKey("test")) return "sync";
                return Settings.DefaultFor(Testing ? "test" : Environment);
            }
        }

        public QueueSettings.Connection Connection(string name)
        {
            name ??= DefaultConnection;
            if (Settings.Connections.TryGetValue(name, out QueueSettings.Connection c)) return c;
            string close = Values.Closest(name, Settings.Connections.Keys);
            throw new KScriptException($"There is no queue connection '{name}'." + (close != null ? $" Did you mean {close}?" : string.Empty)
                + $" Connections: {string.Join(", ", Settings.Connections.Keys)}. Add one under queue.connections in kscript.json.", "Config");
        }

        /// <summary>The driver for a connection; null for sync, which runs jobs as they are dispatched.</summary>
        public IQueueDriver Driver(string name)
        {
            QueueSettings.Connection c = Connection(name);
            return c.Driver switch
            {
                "sync" => null,
                "memory" => MemoryQueueDriver.Named(AppName + "/" + c.Name, c.Name),
                _ => new FileQueueDriver(c.Name, FolderFor(c)),
            };
        }

        /// <summary>Where the file driver keeps a connection's jobs: its path, or queue/&lt;name&gt; in the app's data folder.</summary>
        public string FolderFor(QueueSettings.Connection c) =>
            c.Path != null ? Path.GetFullPath(Path.Combine(Directory ?? ".", c.Path)) : Path.Combine(AppFolders.Folder(AppName, "data"), "queue", AppFolders.CleanName(c.Name));

        /// <summary>The job's file as stored: relative to the project when it is inside it.</summary>
        public string StoredPath(string file)
        {
            if (file == null || Directory == null) return file;
            string relative = Path.GetRelativePath(Directory, file);
            return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? file : relative.Replace('\\', '/');
        }

        public string FullPath(string stored) => stored == null ? null : Path.GetFullPath(Path.IsPathRooted(stored) ? stored : Path.Combine(Directory ?? ".", stored));

        private static readonly ConcurrentDictionary<string, ProjectConfig> Projects = new ConcurrentDictionary<string, ProjectConfig>(StringComparer.Ordinal);

        /// <summary>The context for a running script: the nearest kscript.json to its file, and the run's environment.</summary>
        public static QueueContext For(Interpreter interpreter, Module module)
        {
            string directory = module?.Directory ?? interpreter.Host.WorkingDirectory;
            ProjectConfig project = Projects.GetOrAdd(directory, d =>
            {
                try
                {
                    return ProjectConfig.FindAndLoad(d);
                }
                catch (ProjectConfigException)
                {
                    return null;
                }
            });
            string environment = interpreter.Host.Config?.Environment ?? System.Environment.GetEnvironmentVariable("KSCRIPT_ENV");
            return new QueueContext(project, directory, environment, interpreter.Host.Items.ContainsKey(QueueRuntime.TestSessionKey));
        }

        /// <summary>Forgets the kscript.json files read so far (tests).</summary>
        internal static void ForgetProjects() => Projects.Clear();
    }

    internal static class QueueRuntime
    {
        /// <summary>Seconds a lease lasts past the job's timeout, so a slow worker isn't overtaken by another.</summary>
        public const double LeaseMargin = 30;

        /// <summary>The key KScript.Test keeps its session under: a run with one is a test run.</summary>
        public const string TestSessionKey = "KScript.Test.session";

        /// <summary>The key a fake queue (@fake_queue) is kept under in the host's items.</summary>
        public const string FakeKey = "KScript.Queue.fake";

        /// <summary>The clock queues use. Tests replace it.</summary>
        internal static Func<DateTime> Clock = () => DateTime.UtcNow;

        /// <summary>A failed job, ready to run again from its first try.</summary>
        public static void ResetForRetry(QueuedJob job)
        {
            job.Attempts = 0;
            job.FailedAt = null;
            job.ReservedUntil = null;
            job.AvailableAt = Clock();
            if (job.RetryUntil != null && job.RetryUntil < job.AvailableAt) job.RetryUntil = null;
        }

        /// <summary>The key that makes two dispatches of a unique job the same one: its name and the fields unique_by names.</summary>
        public static string UniqueKey(JobValue job)
        {
            IEnumerable<string> names = job.Type.Options.UniqueBy.Count > 0 ? job.Type.Options.UniqueBy : job.Fields.Keys.OrderBy(k => k, StringComparer.Ordinal);
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string name in names) map[name] = job.Fields.TryGetValue(name, out object v) ? v : null;
            string text = job.Type.Name + ":" + Values.ToJson(map);
            return job.Type.Name + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Substring(0, 24).ToLowerInvariant();
        }

        /// <summary>A job's fields as plain JSON data.</summary>
        public static Dictionary<string, object> Data(JobValue job) =>
            Values.ParseJson(Values.ToJson(job.Fields)) as Dictionary<string, object> ?? new Dictionary<string, object>(StringComparer.Ordinal);
    }
}
