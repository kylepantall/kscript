using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Json;

using KScript.Data;
using KScript.Data.Migrations;
using KScript.Output;
using KScript.Projects;
using KScript.Runtime;
using KScript.Scaffolding;

namespace KScript.Databases
{
    /// <summary>
    /// <c>kscript db diff|migrate|rollback|status|fresh|seed</c>: migrations written from the models, run on the
    /// project's database (database in config, else SQLite at data/app.sqlite), and seeders that fill it.
    /// </summary>
    public static class DbCommand
    {
        public const string Usage = "kscript db diff [name] [--syntax kscript|xml]|migrate|rollback [--steps n]|status [--output text|json]|fresh [--seed]|seed [name] [--pretend] [--force] [--project folder]";

        private static readonly string[] Valued = { "steps", "project", "output", "syntax", "name" };

        public static int Run(string[] args, TextWriter output, TextWriter error, string workingDirectory = null, string environment = null, TextReader input = null, bool? interactive = null, bool startup = false)
        {
            var cli = new CliOutput(output);
            var err = new CliOutput(error);
            foreach (string arg in args)
            {
                if (CliFlags.Retired("db", arg) is string renamed) return BadUsage(err, renamed);
            }
            CliArgs a = CliArgs.Parse(args, Valued);
            if (a.Positional.Count == 0 || a.Flag("help") == true || a.Flag("h") == true)
            {
                output.WriteLine("Usage: " + Usage);
                output.WriteLine("Migrations are in database/migrations, seeders in database/seeders. kscript db diff writes a migration from the models.");
                return a.Positional.Count == 0 && a.Flag("help") != true && a.Flag("h") != true ? ExitCodes.Usage : ExitCodes.Ok;
            }
            string action = a.Positional[0];
            string folder = Path.GetFullPath(a.Value("project") ?? workingDirectory ?? WorkingFolder.Current);
            ProjectConfig project;
            try
            {
                project = ProjectConfig.FindAndLoad(folder);
            }
            catch (ProjectConfigException ex)
            {
                ErrorReport.Write(error, ex);
                return ExitCodes.Usage;
            }
            environment ??= project?.ConfigOptions(null).Environment ?? Environment.GetEnvironmentVariable("KSCRIPT_ENV");
            ConfigOptions config = project?.ConfigOptions(environment);
            if (config != null) config.Root ??= project.Directory;
            var host = new KScriptHost(output, error) { WorkingDirectory = project?.Directory ?? folder, Config = config };
            var migrator = new Migrator(folder, host, project);
            var prompt = new CliPrompt(cli, input, interactive);
            bool production = string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase);

            try
            {
                switch (action)
                {
                    case "diff":
                        return Diff(migrator, a, cli, err, prompt, project);
                    case "migrate":
                        if (startup && migrator.Files().Count == 0) return ExitCodes.Ok;
                        if (production && !Allowed(a, prompt, "migrate the production database")) return Refused(err, "migrate");
                        Migrate(migrator, a, cli);
                        return a.Flag("seed") == true ? Seed(migrator, null, cli) : 0;
                    case "rollback":
                    {
                        if (production && !Allowed(a, prompt, "roll back the production database")) return Refused(err, "rollback");
                        int? steps = null;
                        if (a.Value("steps") is string s)
                        {
                            if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1) return BadUsage(err, "--steps needs a number of migrations, e.g. --steps 2");
                            steps = n;
                        }
                        return Rollback(migrator, steps, a.Flag("all") == true, a.Flag("pretend") == true, cli);
                    }
                    case "status":
                    {
                        string how = (a.Value("output") ?? "text").Trim().ToLowerInvariant();
                        if (how != "text" && how != "json") return BadUsage(err, $"--output should be text or json, not '{how}'.");
                        return Status(migrator, how == "json", cli, output);
                    }
                    case "fresh":
                    {
                        if (production && a.Flag("force") != true) return Fail(err, "db fresh drops every table, so it doesn't run on the production database. Add --force if you really mean it.");
                        if (a.Flag("pretend") == true)
                        {
                            // Nothing is dropped or run: the tables that would go, then the SQL every migration would run.
                            List<string> doomed = migrator.DropAll(pretend: true);
                            cli.NewLine();
                            cli.Info(doomed.Count == 0 ? "The database has no tables to drop." : $"Would drop {Count(doomed.Count, "table")}: {string.Join(", ", doomed)}.");
                            Migrate(migrator, a, cli, fromEmpty: true);
                            return 0;
                        }
                        List<string> dropped = migrator.DropAll();
                        cli.NewLine();
                        cli.Info(dropped.Count == 0 ? "The database had no tables." : $"Dropped {Count(dropped.Count, "table")}.");
                        Migrate(migrator, a, cli);
                        return a.Flag("seed") == true ? Seed(migrator, null, cli) : 0;
                    }
                    case "seed":
                        if (production && !Allowed(a, prompt, "seed the production database")) return Refused(err, "seed");
                        return Seed(migrator, a.Positional.Count > 1 ? a.Positional[1] : null, cli);
                    default:
                        string close = Values.Closest(action, new[] { "diff", "migrate", "rollback", "status", "fresh", "seed" });
                        return BadUsage(err, $"kscript db has no '{action}' command." + (close != null ? $" Did you mean db {close}?" : string.Empty) + " Usage: " + Usage);
                }
            }
            catch (KScriptException ex)
            {
                ErrorReport.Write(error, ex);
                return 1;
            }
            catch (CliPromptException ex)
            {
                return Fail(err, ex.Message);
            }
        }

        private static bool Allowed(CliArgs a, CliPrompt prompt, string what) =>
            a.Flag("force") == true || prompt.Confirm($"The environment is production. Do you really want to {what}?", false);

        private static int Refused(CliOutput err, string command) =>
            Fail(err, $"Stopped: the environment is production. Run kscript db {command} --force to do it anyway.");

        /// <summary>The work could not be done: exit code 1.</summary>
        private static int Fail(CliOutput err, string message)
        {
            err.Error(message);
            return ExitCodes.Failed;
        }

        /// <summary>The command line can't be run as written: exit code 2.</summary>
        private static int BadUsage(CliOutput err, string message)
        {
            err.Error(message);
            return ExitCodes.Usage;
        }

        private static string Count(int n, string what) => n.ToString(CultureInfo.InvariantCulture) + " " + what + (n == 1 ? string.Empty : "s");

        #region diff

        private static int Diff(Migrator migrator, CliArgs a, CliOutput cli, CliOutput err, CliPrompt prompt, ProjectConfig project)
        {
            List<ModelInfo> models = migrator.Models();
            if (models.Count == 0)
                return Fail(err, $"There are no models in {migrator.Relative(migrator.ModelsFolder)} to write a migration for. Make one with kscript make model Post.");
            Schema current = migrator.Replay(migrator.Files());
            Schema target = Schema.FromModels(models);
            // Where it can't ask, a rename is taken as a drop and an add, and said after.
            var unasked = new List<string>();
            List<Step> steps = Differ.Diff(current, target, (kind, table, from, to) =>
            {
                string what = kind == "table" ? $"the table {from} to {to}" : $"{table}.{from} to {to}";
                if (!prompt.Interactive)
                {
                    unasked.Add(what);
                    return false;
                }
                return prompt.Confirm(kind == "table" ? $"Was the table {from} renamed to {to}? (No drops {from} and creates {to}.)" : $"Was {table}.{from} renamed to {to}? (No drops {from} and adds {to}.)", false);
            });
            if (steps.Count == 0)
            {
                cli.NewLine();
                cli.Info("Nothing to write: the migrations already make the tables the models want.");
                return 0;
            }
            string name = StubNames.Snake(a.Positional.Count > 1 ? a.Positional[1] : Differ.NameFor(steps));
            if (name.Length == 0) name = Differ.NameFor(steps);
            string text = MigrationWriter.File(steps, $"Written by kscript db diff from the models. Read it, change it if you need to, then run kscript db migrate.");
            string file = Path.Combine(migrator.Relative(migrator.MigrationsFolder), KScript.Scaffolding.Scaffolder.Clock().ToString("yyyy_MM_dd_HHmmss", CultureInfo.InvariantCulture) + "_" + name + ".kscript").Replace('\\', '/');
            string format = ScriptFormat.Resolve(a.Value(ScriptFormat.Option) is string f ? new Dictionary<string, string> { [ScriptFormat.Option] = f } : null, project);
            (file, text) = ScriptFormat.Apply("migration.kscript", file, text, format);
            if (a.Flag("pretend") == true)
            {
                cli.Line(text.TrimEnd('\n'));
                return 0;
            }
            string full = Path.Combine(migrator.Directory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text);
            cli.NewLine();
            cli.Success($"Wrote {file}, with {Count(steps.Count, "step")}.");
            cli.Bullets(steps.Select(s => s.Describe()));
            foreach (string what in unasked)
            {
                cli.NewLine();
                cli.Warn($"If you renamed {what}, the migration drops it and loses its rows. Run kscript db diff where it can ask, or change those steps to a rename.");
            }
            cli.NewLine();
            cli.Comment("  Read it, then run kscript db migrate.");
            return 0;
        }

        #endregion

        #region migrate and rollback

        private static void Migrate(Migrator migrator, CliArgs a, CliOutput cli, bool fromEmpty = false)
        {
            bool pretend = a.Flag("pretend") == true;
            cli.NewLine();
            string last = null;
            migrator.Pretended = (file, sql) =>
            {
                if (file.Name != last) cli.Line(cli.Style.Yellow("-- " + file.Name));
                last = file.Name;
                cli.Line(sql + ";");
            };
            migrator.Done = (file, time) => cli.TaskLine(file.Name, true, time);
            if (!pretend) cli.Info("Running migrations.");
            List<MigrationFile> ran = migrator.Migrate(pretend, fromEmpty);
            if (ran.Count == 0) cli.Info("Nothing to migrate: every migration has run.");
            else if (pretend) cli.NewLine();
        }

        private static int Rollback(Migrator migrator, int? steps, bool all, bool pretend, CliOutput cli)
        {
            cli.NewLine();
            string last = null;
            migrator.Pretended = (file, sql) =>
            {
                if (file.Name != last) cli.Line(cli.Style.Yellow("-- undo " + file.Name));
                last = file.Name;
                cli.Line(sql + ";");
            };
            migrator.Done = (file, time) => cli.TaskLine(file.Name, true, time);
            if (!pretend) cli.Info("Rolling back migrations.");
            List<MigrationFile> undone = migrator.Rollback(steps, pretend, all);
            if (undone.Count == 0) cli.Info("Nothing to roll back: no migration has run.");
            return 0;
        }

        #endregion

        #region status

        private static int Status(Migrator migrator, bool json, CliOutput cli, TextWriter output)
        {
            List<AppliedMigration> applied = migrator.Applied();
            var byName = applied.ToDictionary(r => r.Name, StringComparer.Ordinal);
            List<MigrationFile> files = migrator.Files();
            var rows = files.Select(f => byName.TryGetValue(f.Name, out AppliedMigration r)
                ? (Name: f.Name, Ran: true, Batch: (int?)r.Batch, RanAt: r.RanAt, Missing: false)
                : (Name: f.Name, Ran: false, Batch: (int?)null, RanAt: (string)null, Missing: false)).ToList();
            rows.AddRange(applied.Where(r => files.All(f => f.Name != r.Name)).Select(r => (Name: r.Name, Ran: true, Batch: (int?)r.Batch, RanAt: r.RanAt, Missing: true)));
            List<Step> drift = null;
            List<ModelInfo> models = migrator.Models();
            if (models.Count > 0) drift = Differ.Diff(migrator.Replay(migrator.Ordered(applied)), Schema.FromModels(models));

            if (json)
            {
                // Written field by field, so it needs no reflection (kscript is a Native AOT build).
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                {
                    w.WriteStartObject();
                    w.WriteString("database", migrator.Database.ToText());
                    w.WriteStartArray("migrations");
                    foreach (var r in rows)
                    {
                        w.WriteStartObject();
                        w.WriteString("name", r.Name);
                        w.WriteBoolean("ran", r.Ran);
                        if (r.Batch is int batch) w.WriteNumber("batch", batch);
                        else w.WriteNull("batch");
                        w.WriteString("ran_at", r.RanAt);
                        w.WriteBoolean("file_missing", r.Missing);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteStartArray("model_changes");
                    foreach (Step step in drift ?? new List<Step>()) w.WriteStringValue(step.Describe());
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                output.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                return 0;
            }

            cli.NewLine();
            cli.Info(migrator.Database.ToText() + ".");
            if (rows.Count == 0)
            {
                cli.Info($"There are no migrations in {migrator.Relative(migrator.MigrationsFolder)} yet. kscript db diff writes one from the models.");
            }
            else
            {
                cli.NewLine();
                foreach (var r in rows)
                {
                    string state = r.Missing ? cli.Style.Red($"Ran (batch {r.Batch}), file missing")
                        : r.Ran ? cli.Style.Green($"Ran (batch {r.Batch})") : cli.Style.Yellow("Pending");
                    cli.TwoColumn(r.Name, state);
                }
            }
            cli.NewLine();
            if (drift?.Count > 0)
            {
                cli.Warn($"The models want {Count(drift.Count, "change")} the migrations don't make yet. kscript db diff writes them:");
                cli.Bullets(drift.Select(s => s.Describe()));
                cli.NewLine();
            }
            return 0;
        }

        #endregion

        #region seed

        private static int Seed(Migrator migrator, string name, CliOutput cli)
        {
            List<string> seeders = migrator.Seeders(name);
            cli.NewLine();
            if (seeders.Count == 0)
            {
                cli.Info($"There are no seeders in {migrator.Relative(migrator.SeedersFolder)}. Make one with kscript make seeder users.");
                return 0;
            }
            cli.Info("Seeding the database.");
            foreach (string path in seeders)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Interpreter interpreter = migrator.NewInterpreter();
                int code = interpreter.RunFile(path);
                cli.TaskLine(ProjectConfig.ScriptName(path), code == 0, clock.Elapsed);
                if (code != 0) return code;
            }
            cli.NewLine();
            return 0;
        }

        #endregion
    }
}
