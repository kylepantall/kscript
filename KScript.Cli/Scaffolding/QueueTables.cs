using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using KScript.Cli;
using KScript.Language.Formatting;
using KScript.Projects;
using KScript.Syntax.Markup;

namespace KScript.Scaffolding
{
    /// <summary>
    /// <c>kscript publish queue</c>: writes the migration that makes the database queue's tables, and sets the queue
    /// connection's auto_create to false, so the project's migrations make the tables instead of the driver.
    /// </summary>
    public static class QueueTables
    {
        /// <summary>The name kscript publish takes.</summary>
        public const string Name = "queue";

        /// <summary>What kscript publish lists for it.</summary>
        public const string Summary = "The tables the database queue keeps its jobs, failed jobs and locks in, as a migration.";

        private const string FileSuffix = "_create_queue_tables";

        /// <summary>The migration's .kscript text for these table names.</summary>
        public static string Migration(string connection, string jobs, string failed, string locks) => $$"""
            // Written by kscript publish queue: the tables the {{connection}} queue keeps its jobs in.
            // To rename one, change it here and in queue.connections.{{connection}}.tables in kscript.json,
            // then run kscript db migrate.
            import KScript.Data

            Migration {
              CreateTable(name: '{{jobs}}') {
                Field(name: 'id', type: string, key)
                Field(name: 'queue', type: string)
                Field(name: 'available_at', type: int)
                Field(name: 'reserved_until', type: int, optional)
                Field(name: 'payload', type: string)
                Index(columns: [queue, available_at])
              }
              CreateTable(name: '{{failed}}') {
                Field(name: 'id', type: string, key)
                Field(name: 'queue', type: string)
                Field(name: 'failed_at', type: int)
                Field(name: 'payload', type: string)
              }
              CreateTable(name: '{{locks}}') {
                Field(name: 'key', type: string, key)
                Field(name: 'until', type: int)
              }
            }

            """.Replace("\r\n", "\n");

        /// <summary>The queue tables' migration the project already has, relative to the project, or null.</summary>
        public static string Existing(string root, ProjectConfig project)
        {
            string folder = Path.Combine(root, project?.PathFor("migrations") ?? ProjectConfig.DefaultPaths["migrations"]);
            if (!Directory.Exists(folder)) return null;
            string found = Directory.EnumerateFiles(folder)
                .FirstOrDefault(f => Path.GetFileName(f).Split('.')[0].EndsWith(FileSuffix, StringComparison.Ordinal));
            return found != null ? Path.GetRelativePath(root, found).Replace('\\', '/') : null;
        }

        /// <summary>
        /// Writes the migration and the setting (unless <paramref name="dryRun"/>). Returns the migration's path and
        /// kscript.json's, relative to the project.
        /// </summary>
        public static (string Migration, string Config) Publish(string root, string connectionName, IReadOnlyDictionary<string, string> options, bool force, bool dryRun)
        {
            root = Path.GetFullPath(root);
            ProjectConfig project = ProjectConfig.FindAndLoad(root);
            connectionName ??= "database";
            if (!project.Queue.Connections.TryGetValue(connectionName, out QueueSettings.Connection connection))
                throw new StubException($"There is no queue connection called {connectionName} in kscript.json. Name one with --connection.");
            if (connection.Driver != "database")
                throw new StubException($"The {connectionName} queue uses the {connection.Driver} driver, which keeps no tables. kscript publish queue is for a connection with \"driver\": \"database\".");
            if (connection.Database != null && connection.Database != "main")
                throw new StubException($"The {connectionName} queue keeps its jobs in the {connection.Database} database, and kscript db migrate runs on the default one. Leave its tables to the driver.");

            string existing = Existing(root, project);
            if (existing != null && !force)
                throw new StubException($"This project already has the queue tables' migration in {existing}. Use --force to write it again.");

            string folder = (project.PathFor("migrations") ?? ProjectConfig.DefaultPaths["migrations"]).Replace('\\', '/').Trim('/');
            string rel = existing ?? folder + "/" + Scaffolder.Clock().ToString("yyyy_MM_dd_HHmmss", CultureInfo.InvariantCulture) + FileSuffix + ".kscript";
            string text = Migration(connectionName, connection.JobsTable, connection.FailedTable, connection.LocksTable);
            string format = existing != null ? (MarkupParser.IsCodePath(existing) ? ScriptFormat.KScript : ScriptFormat.Xml) : ScriptFormat.Resolve(options, project);
            (rel, text) = ScriptFormat.Apply("migration.kscript", rel, text, format, FormatOptions.From(project));

            string configFile = project.FilePath;
            string json = File.ReadAllText(configFile).Replace("\r\n", "\n");
            string[] at = { "queue", "connections", connectionName };
            Func<Exception> notObject = () => new StubException("The queue and its connections in kscript.json must be objects.");
            if (!HasConnection(json, connectionName)) json = JsonConfigEdit.SetProperty(json, at.Append("driver").ToArray(), JsonConfigEdit.Quote("database"), notObject);
            json = JsonConfigEdit.SetProperty(json, at.Append("auto_create").ToArray(), "false", notObject);

            if (!dryRun)
            {
                string full = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
                File.WriteAllText(configFile, json);
            }
            return (rel, Path.GetRelativePath(root, configFile).Replace('\\', '/'));
        }

        private static bool HasConnection(string json, string name)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions
                {
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true,
                });
                return doc.RootElement.TryGetProperty("queue", out var q) && q.ValueKind == System.Text.Json.JsonValueKind.Object
                    && q.TryGetProperty("connections", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Object
                    && c.TryGetProperty(name, out _);
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }
    }
}
