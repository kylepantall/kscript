using System.Collections.Concurrent;

using KScript.Projects;
using KScript.Runtime;

namespace KScript.Data.Migrations
{
    /// <summary>
    /// Models whose tables the migrations don't make as they are now, for the editor and kscript check (KS5014). The
    /// files on disk are read (the models and migrations are run, as kscript db diff does), and the answer is kept
    /// until one of them changes.
    /// </summary>
    public static class Drift
    {
        private static readonly ConcurrentDictionary<string, (string Stamp, IReadOnlyDictionary<string, string> Models)> Cache =
            new ConcurrentDictionary<string, (string, IReadOnlyDictionary<string, string>)>(StringComparer.Ordinal);

        private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

        /// <summary>
        /// For the project around <paramref name="folder"/>: each model that has drifted, by name, with what the
        /// migration for it would do ("add field users.bio"). Empty when the project has no migrations yet, and when
        /// the models or migrations can't be run (their own errors say why).
        /// </summary>
        public static IReadOnlyDictionary<string, string> Of(string folder)
        {
            ProjectConfig project;
            try
            {
                project = ProjectConfig.FindAndLoad(folder);
            }
            catch (ProjectConfigException)
            {
                return None;
            }
            if (project?.FilePath == null) return None;
            var migrator = new Migrator(project.Directory, KScriptHost.ForText(new StringWriter()), project);
            List<string> migrations = Migrator.Scripts(migrator.MigrationsFolder);
            if (migrations.Count == 0) return None;
            List<string> files = migrations.Concat(migrator.ModelScripts()).Append(project.FilePath).ToList();
            string stamp = string.Join("\n", files.Select(f => f + "|" + File.GetLastWriteTimeUtc(f).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (Cache.TryGetValue(project.Directory, out var known) && known.Stamp == stamp) return known.Models;

            IReadOnlyDictionary<string, string> found;
            try
            {
                found = Find(migrator);
            }
            catch (KScriptException)
            {
                found = None;
            }
            Cache[project.Directory] = (stamp, found);
            return found;
        }

        private static IReadOnlyDictionary<string, string> Find(Migrator migrator)
        {
            Schema current = migrator.Replay(migrator.Files());
            List<ModelInfo> models = migrator.Models();
            Schema target = Schema.FromModels(models);
            var drifted = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ModelInfo model in models)
            {
                var before = new Schema();
                if (current.Table(model.Table) is TableSpec made) before.Add(made.Clone());
                var after = new Schema();
                after.Add(target.Table(model.Table).Clone());
                List<Step> steps = Differ.Diff(before, after);
                if (steps.Count == 0) continue;
                string first = steps[0].Describe();
                drifted[model.Name] = steps.Count == 1 ? first : $"{first}, and {steps.Count - 1} more change{(steps.Count == 2 ? string.Empty : "s")}";
            }
            return drifted;
        }
    }
}
