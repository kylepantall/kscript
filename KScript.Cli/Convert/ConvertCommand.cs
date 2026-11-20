using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Conversion
{
    /// <summary>
    /// kscript convert &lt;file-or-folder&gt;... [--to xml|kscript] [--out path] [--stdout] [--force]:
    /// writes the .kscript version of an XML script (x.ks.xml gives x.kscript) or the XML version of a .kscript file.
    /// The original is never changed.
    /// </summary>
    public static class ConvertCommand
    {
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            var inputs = new List<string>();
            string to = null;
            string outPath = null;
            bool toStdout = false;
            bool force = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--to":
                        if (i + 1 >= args.Length || args[i + 1] is not ("xml" or "kscript"))
                        {
                            error.WriteLine("--to needs xml or kscript, e.g. --to kscript");
                            return 1;
                        }
                        to = args[++i];
                        break;
                    case "--out":
                    case "-o":
                        if (i + 1 >= args.Length)
                        {
                            error.WriteLine("--out needs a file name.");
                            return 1;
                        }
                        outPath = args[++i];
                        break;
                    case "--stdout":
                        toStdout = true;
                        break;
                    case "--force":
                    case "-f":
                        force = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage(output);
                        return 0;
                    default:
                        if (args[i].StartsWith("-", StringComparison.Ordinal))
                        {
                            error.WriteLine($"Unknown option '{args[i]}'. Run kscript convert --help.");
                            return 1;
                        }
                        inputs.Add(args[i]);
                        break;
                }
            }

            if (inputs.Count == 0)
            {
                PrintUsage(error);
                return 1;
            }

            List<string> files = new List<string>();
            foreach (string input in inputs)
            {
                if (Directory.Exists(input))
                {
                    files.AddRange(Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories)
                        .Where(f => to == "xml" ? MarkupParser.IsCodePath(f) : to == "kscript" ? IsXmlScript(f) : MarkupParser.IsCodePath(f) || IsXmlScript(f))
                        .OrderBy(f => f, StringComparer.Ordinal));
                }
                else if (File.Exists(input))
                {
                    files.Add(input);
                }
                else
                {
                    error.WriteLine($"There is no file or folder at '{input}'.");
                    return 2;
                }
            }

            if (outPath != null && files.Count != 1)
            {
                error.WriteLine("--out works with one file. For a folder, each file is written next to its original.");
                return 1;
            }

            // Every file this run converts, so imports between them can point at the new files.
            var converting = new HashSet<string>(files.Select(f => Path.GetFullPath(f)), PathComparer);
            // Folders a file name in a comment or string may be relative to, besides the file's own folder.
            var roots = inputs.Where(Directory.Exists).Select(Path.GetFullPath).ToList();

            int failures = 0;
            foreach (string file in files)
            {
                bool isCode = MarkupParser.IsCodePath(file);
                if ((to == "xml" && !isCode) || (to == "kscript" && isCode))
                {
                    error.WriteLine($"{file} is already {(isCode ? ".kscript" : "XML")}; skipped.");
                    continue;
                }

                string text = File.ReadAllText(file);
                MarkupDocument parsed = isCode ? CodeParser.Parse(text) : MarkupParser.Parse(text, DocumentKind.Script);
                Diagnostic syntax = parsed.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
                if (syntax != null)
                {
                    var position = parsed.Source.GetPosition(syntax.Span.Start);
                    error.WriteLine($"{file}({position.Line + 1},{position.Character + 1}): {syntax.Message} Fix it before converting.");
                    failures++;
                    continue;
                }

                var notConverted = new List<string>();
                Func<string, string> importPath = src => ImportPath(file, src, toCode: !isCode, converting, notConverted);
                ConversionResult result = isCode ? ScriptConverter.ToXml(parsed, importPath, LanguageCatalog.Default.IsNameList) : ScriptConverter.ToCode(parsed, importPath, LanguageCatalog.Default.IsNameList);
                string written = RenameMentions(result.Text, file, toCode: !isCode, converting, roots);
                foreach (string src in notConverted.Distinct())
                {
                    error.WriteLine($"{file}: imports {src}, which has no {(isCode ? "XML" : ".kscript")} version yet, so the import is kept as it is. " +
                                    $"Convert that file too, or the whole folder, to keep every import in one format.");
                }
                foreach (string warning in result.Warnings)
                {
                    error.WriteLine($"{file}: {warning}");
                }

                if (toStdout)
                {
                    output.Write(written);
                    continue;
                }

                string target = outPath ?? ScriptConverter.ConvertedPath(file);
                if (File.Exists(target) && !force)
                {
                    error.WriteLine($"{target} already exists. Use --force to replace it, or --out to pick another name.");
                    failures++;
                    continue;
                }
                File.WriteAllText(target, written);
                output.WriteLine($"{file} -> {target}");
            }
            if (!toStdout && outPath == null)
            {
                UpdateProjectFiles(files, converting, roots, output);
            }
            return failures > 0 ? 1 : 0;
        }

        /// <summary>
        /// After converting to .kscript, the kscript.json of each project that had scripts converted names the new
        /// files: "main" and other paths are renamed, and include patterns for *.ks.xml gain a *.kscript twin.
        /// </summary>
        private static void UpdateProjectFiles(List<string> files, HashSet<string> converting, List<string> roots, TextWriter output)
        {
            var projects = files.Select(f => KScript.Projects.ProjectConfig.Find(Path.GetDirectoryName(Path.GetFullPath(f))))
                .Where(p => p != null).Distinct(PathComparer).ToList();
            foreach (string project in projects)
            {
                string original = File.ReadAllText(project);
                bool toCode = files.Any(f => !MarkupParser.IsCodePath(f));
                string text = RenameMentions(original, project, toCode, converting, roots);
                if (toCode)
                {
                    text = Glob.Replace(text, m =>
                    {
                        string twin = m.Groups[1].Value + "*." + m.Groups[2].Value + "kscript";
                        return text.Contains("\"" + twin + "\"", StringComparison.Ordinal) ? m.Value : m.Value + ", \"" + twin + "\"";
                    });
                }
                if (text != original)
                {
                    File.WriteAllText(project, text);
                    output.WriteLine($"{project}: now names the converted files");
                }
            }

            // Pages in a converted folder import the scripts too (<import src="lib/tasks.ks.xml" />), and a project's
            // Makefile runs them (kscript desktop.ks.xml).
            foreach (string page in roots.SelectMany(r => Directory.EnumerateFiles(r, "*", SearchOption.AllDirectories))
                         .Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(Path.GetFileName(f), "Makefile", StringComparison.Ordinal))
                         .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "node_modules" or ".git" or "_templates"))
                         .Distinct(PathComparer).OrderBy(f => f, StringComparer.Ordinal))
            {
                string original = File.ReadAllText(page);
                string text = RenameMentions(original, page, files.Any(f => !MarkupParser.IsCodePath(f)), converting, roots);
                if (text != original)
                {
                    File.WriteAllText(page, text);
                    output.WriteLine($"{page}: now names the converted files");
                }
            }

            // Agent docs describe the converted scripts, in the new format.
            bool code = files.Any(f => !MarkupParser.IsCodePath(f));
            foreach (string root in projects.Select(Path.GetDirectoryName).Where(global::KScript.Language.Agents.AgentDocs.IsEnabled))
            {
                if (global::KScript.Language.Agents.AgentDocs.Write(root, format: code ? "kscript" : "xml", skip: converting).Count > 0)
                    output.WriteLine($"{Path.Combine(root, global::KScript.Language.Agents.AgentDocs.AgentsFile)}: updated for the converted files");
            }
        }

        private static readonly System.Text.RegularExpressions.Regex Glob = new System.Text.RegularExpressions.Regex(@"""([^""\s]*)\*\.((?:test\.)?)ks\.xml""");

        private static StringComparer PathComparer =>
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        /// <summary>
        /// The src a converted import gets. An import of a script in the other format points at that script's
        /// converted version when this run writes it or it already exists; anything else is kept as written.
        /// </summary>
        private static string ImportPath(string file, string src, bool toCode, HashSet<string> converting, List<string> notConverted)
        {
            if (string.IsNullOrEmpty(src) || src.Contains("://", StringComparison.Ordinal) || src.Contains('{') || src.Contains('$'))
            {
                return src;
            }
            bool srcIsCode = MarkupParser.IsCodePath(src);
            if (srcIsCode == toCode || !(srcIsCode || IsXmlScriptName(src)))
            {
                return src;
            }
            string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)), src));
            if (converting.Contains(full) || File.Exists(ScriptConverter.ConvertedPath(full)))
            {
                return ScriptConverter.ConvertedPath(src);
            }
            notConverted.Add(src);
            return src;
        }

        private static readonly System.Text.RegularExpressions.Regex Mention = new System.Text.RegularExpressions.Regex(
            @"(?<![\w/.\-])((?:\.\.?/)*[\w\-]+(?:/[\w\-.]+)*?\.(?:ks\.xml|kscript|ks|xml))(?![\w\-/]|\.\w)");

        /// <summary>
        /// File names in comments and strings (lib/tasks.ks.xml, '../scripts/hello.ks.xml') that name a script this
        /// run converts, or that already has a converted version, are changed to the new name.
        /// </summary>
        private static string RenameMentions(string text, string file, bool toCode, HashSet<string> converting, List<string> roots)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(file));
            return Mention.Replace(text, m =>
            {
                string name = m.Groups[1].Value;
                if (MarkupParser.IsCodePath(name) == toCode || name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
                foreach (string root in roots.Prepend(dir))
                {
                    string full = Path.GetFullPath(Path.Combine(root, name));
                    if (converting.Contains(full) || File.Exists(ScriptConverter.ConvertedPath(full)))
                    {
                        return ScriptConverter.ConvertedPath(name);
                    }
                }
                return name;
            });
        }

        private static bool IsXmlScriptName(string path) =>
            path.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ks", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

        private static bool IsXmlScript(string path) =>
            path.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ks", StringComparison.OrdinalIgnoreCase);

        private static void PrintUsage(TextWriter writer)
        {
            writer.WriteLine("Usage: kscript convert <file-or-folder>... [--to xml|kscript] [--out file] [--stdout] [--force]");
            writer.WriteLine("  Writes the .kscript version of an XML script (x.ks.xml -> x.kscript), or the XML version of a .kscript file.");
            writer.WriteLine("  The original is left as it is. A folder converts every script in it (*.ks.xml, *.ks, *.kscript, tests too).");
            writer.WriteLine("  Imports, and file names in comments and strings, that point at a converted script are changed to its new name.");
        }
    }
}
