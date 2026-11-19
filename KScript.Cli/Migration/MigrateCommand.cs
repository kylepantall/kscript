using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KScript.Migration
{
    /// <summary>
    /// <c>kscript migrate &lt;file-or-folder&gt;... [--dry-run] [--no-backup] [--force]</c>: rewrites KScript 1 scripts
    /// as KScript 2. Each file is rewritten in place and the original is kept next to it as <c>name.xml.v1.bak</c>.
    /// </summary>
    public static class MigrateCommand
    {
        public const string BackupSuffix = ".v1.bak";

        private static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules", ".git", ".vs", ".vscode" };

        public static void PrintUsage(TextWriter output)
        {
            output.WriteLine("Usage: kscript migrate <file-or-folder>... [--dry-run] [--no-backup] [--force]");
            output.WriteLine();
            output.WriteLine("Rewrites KScript 1 scripts as KScript 2. Each file is changed in place, and the original is");
            output.WriteLine($"kept next to it as <name>{BackupSuffix}. Anything that can't be converted is left as it was, with");
            output.WriteLine($"a <!-- {Migrator.CommentPrefix} ... --> comment above it, and listed when the command finishes.");
            output.WriteLine();
            output.WriteLine("  --dry-run    Print the converted script instead of changing the file.");
            output.WriteLine("  --no-backup  Don't keep a copy of the original.");
            output.WriteLine("  --force      Convert files that already look like KScript 2.");
        }

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            bool dryRun = args.Contains("--dry-run");
            bool backup = !args.Contains("--no-backup");
            bool force = args.Contains("--force");
            var paths = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();

            if (args.Contains("--help") || args.Contains("-h"))
            {
                PrintUsage(output);
                return 0;
            }

            foreach (string option in args.Where(a => a.StartsWith("--", StringComparison.Ordinal) && a is not ("--dry-run" or "--no-backup" or "--force")))
            {
                error.WriteLine($"Unknown option '{option}'. Run kscript migrate --help for usage.");
                return 1;
            }

            if (paths.Count == 0)
            {
                PrintUsage(error);
                return 1;
            }

            var files = new List<(string Path, bool Named)>();
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    files.Add((Path.GetFullPath(path), true));
                }
                else if (Directory.Exists(path))
                {
                    files.AddRange(FindScripts(path).Select(f => (f, false)));
                }
                else
                {
                    error.WriteLine($"There is no file or folder at '{path}'.");
                    return 1;
                }
            }

            int migrated = 0, current = 0, notes = 0;
            bool printScript = dryRun && files.Count == 1;
            foreach ((string file, bool named) in files)
            {
                string text = File.ReadAllText(file);
                MigrationResult result = Migrator.Migrate(text, force);
                string display = DisplayPath(file);

                if (!result.WasV1)
                {
                    current++;
                    if (named)
                    {
                        string reason = result.Notes.Count > 0 ? result.Notes[0].Message : "it already looks like KScript 2 (use --force to convert it anyway).";
                        error.WriteLine($"{display}: skipped, {char.ToLowerInvariant(reason[0])}{reason.Substring(1)}");
                    }
                    continue;
                }

                migrated++;
                notes += result.Notes.Count;
                if (printScript)
                {
                    output.Write(result.Text);
                    if (!result.Text.EndsWith("\n", StringComparison.Ordinal)) output.WriteLine();
                }
                else if (dryRun)
                {
                    output.WriteLine($"==> {display}");
                    output.WriteLine(result.Text);
                }
                else
                {
                    if (backup)
                    {
                        string copy = file + BackupSuffix;
                        if (!File.Exists(copy)) File.WriteAllText(copy, text);
                    }
                    File.WriteAllText(file, result.Text, new UTF8Encoding(false));
                    error.WriteLine($"Migrated {display}" + (result.Notes.Count == 0 ? "" : $" ({Plural(result.Notes.Count, "note")})"));
                }

                foreach (MigrationNote note in result.Notes)
                {
                    error.WriteLine($"  {display}:{note.Line}: {note.Message}");
                }
            }

            if (files.Count > 1 || files.Count == 0)
            {
                error.WriteLine($"{Plural(migrated, "script")} migrated, {Plural(notes, "note")}" + (current > 0 ? $", {current} already KScript 2." : "."));
            }
            if (migrated > 0 && !dryRun)
            {
                error.WriteLine("Run kscript check to look over the result.");
            }
            return 0;
        }

        /// <summary>The .xml files under a folder whose root element is &lt;kscript&gt;.</summary>
        internal static IEnumerable<string> FindScripts(string folder)
        {
            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(folder));
            var found = new List<string>();
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                foreach (string sub in Directory.EnumerateDirectories(dir).OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (!SkippedFolders.Contains(Path.GetFileName(sub))) pending.Push(sub);
                }
                foreach (string file in Directory.EnumerateFiles(dir, "*.xml").OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (IsScript(file)) found.Add(file);
                }
            }
            return found.OrderBy(x => x, StringComparer.Ordinal);
        }

        private static bool IsScript(string file)
        {
            try
            {
                using var reader = new StreamReader(file);
                char[] buffer = new char[4096];
                int read = reader.ReadBlock(buffer, 0, buffer.Length);
                string head = new string(buffer, 0, read);
                int i = 0;
                while (i < head.Length)
                {
                    if (char.IsWhiteSpace(head[i]) || head[i] == '﻿') { i++; continue; }
                    if (head.AsSpan(i).StartsWith("<?") || head.AsSpan(i).StartsWith("<!--"))
                    {
                        int end = head.IndexOf(head[i + 1] == '?' ? "?>" : "-->", i, StringComparison.Ordinal);
                        if (end < 0) return false;
                        i = end + (head[i + 1] == '?' ? 2 : 3);
                        continue;
                    }
                    return head.AsSpan(i).StartsWith("<kscript") && (i + 8 >= head.Length || !char.IsLetterOrDigit(head[i + 8]));
                }
            }
            catch (IOException)
            {
            }
            return false;
        }

        private static string DisplayPath(string file)
        {
            string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), file);
            return relative.StartsWith("..", StringComparison.Ordinal) ? file : relative;
        }

        private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
    }
}
