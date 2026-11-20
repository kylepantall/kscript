using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

                ConversionResult result = isCode ? ScriptConverter.ToXml(parsed) : ScriptConverter.ToCode(parsed);
                foreach (string warning in result.Warnings)
                {
                    error.WriteLine($"{file}: {warning}");
                }

                if (toStdout)
                {
                    output.Write(result.Text);
                    continue;
                }

                string target = outPath ?? ScriptConverter.ConvertedPath(file);
                if (File.Exists(target) && !force)
                {
                    error.WriteLine($"{target} already exists. Use --force to replace it, or --out to pick another name.");
                    failures++;
                    continue;
                }
                File.WriteAllText(target, result.Text);
                output.WriteLine($"{file} -> {target}");
            }
            return failures > 0 ? 1 : 0;
        }

        private static bool IsXmlScript(string path) =>
            (path.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ks", StringComparison.OrdinalIgnoreCase))
            && !KScript.Projects.TestSettings.IsTestFileName(path);

        private static void PrintUsage(TextWriter writer)
        {
            writer.WriteLine("Usage: kscript convert <file-or-folder>... [--to xml|kscript] [--out file] [--stdout] [--force]");
            writer.WriteLine("  Writes the .kscript version of an XML script (x.ks.xml -> x.kscript), or the XML version of a .kscript file.");
            writer.WriteLine("  The original is left as it is. A folder converts every script in it (*.ks.xml, *.ks, *.kscript; not tests).");
        }
    }
}
