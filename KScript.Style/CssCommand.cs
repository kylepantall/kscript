using KScript.Runtime;

namespace KScript.Style
{
    /// <summary>
    /// kscript css [file-or-folder]... [--out folder] [--var name=value]: compiles .ks.css, .ks.scss and .ks.sass
    /// stylesheets. Folders are searched for them, skipping node_modules, .git and files starting with _ (partials).
    /// </summary>
    public static class CssCommand
    {
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            var inputs = new List<string>();
            var vars = new Dictionary<string, object>();
            string outDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out":
                        if (i + 1 >= args.Length) return Fail(error, "--out needs a folder, e.g. --out public/css");
                        outDir = args[++i];
                        break;
                    case "--var":
                        if (i + 1 >= args.Length || !args[i + 1].Contains('=')) return Fail(error, "--var needs name=value, e.g. --var dark=true");
                        string pair = args[++i];
                        int eq = pair.IndexOf('=');
                        vars[pair.Substring(0, eq)] = ConfigLoader.Scalar(pair.Substring(eq + 1));
                        break;
                    default:
                        if (args[i].StartsWith("-", StringComparison.Ordinal)) return Fail(error, $"Unknown option '{args[i]}'. Usage: kscript css [file-or-folder]... [--out folder] [--var name=value]");
                        inputs.Add(args[i]);
                        break;
                }
            }
            if (inputs.Count == 0) inputs.Add(".");

            var jobs = new List<(string Source, string Target)>();
            foreach (string input in inputs)
            {
                string full = Path.GetFullPath(input);
                if (File.Exists(full))
                {
                    if (!Stylesheets.IsStylesheet(full)) return Fail(error, $"'{input}' isn't a stylesheet: its name should end in .ks.css, .ks.scss or .ks.sass.");
                    jobs.Add((full, Target(full, Path.GetDirectoryName(full), outDir)));
                }
                else if (Directory.Exists(full))
                {
                    foreach (string file in Find(full)) jobs.Add((file, Target(file, full, outDir)));
                }
                else
                {
                    return Fail(error, $"There is no file or folder at '{input}'.");
                }
            }
            // Two stylesheets that would be written to one file: stop before either is, so none is lost unnoticed.
            var clash = jobs.Distinct().GroupBy(job => job.Target, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
            if (clash != null)
            {
                string Shown(string path) => Path.GetRelativePath(KScript.Runtime.WorkingFolder.Current, path);
                return Fail(error, $"{string.Join(" and ", clash.Select(job => Shown(job.Source)))} would both be written to {Shown(clash.Key)}. Compile them one at a time, or give each its own --out folder.");
            }
            if (jobs.Count == 0)
            {
                output.WriteLine("No stylesheets found (files ending in .ks.css, .ks.scss or .ks.sass).");
                return 0;
            }

            int failed = 0;
            foreach (var (source, target) in jobs)
            {
                try
                {
                    string css = Stylesheets.Compile(source, vars);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllText(target, css);
                    output.WriteLine($"{Path.GetRelativePath(KScript.Runtime.WorkingFolder.Current, source)} -> {Path.GetRelativePath(KScript.Runtime.WorkingFolder.Current, target)}");
                }
                catch (KScriptException ex)
                {
                    failed++;
                    KScript.Runtime.ErrorReport.Write(error, ex);
                }
            }
            return failed == 0 ? 0 : 3;
        }

        private static int Fail(TextWriter error, string message)
        {
            new KScript.Output.CliOutput(error).Error(message);
            return ExitCodes.Usage;
        }

        private static string Target(string source, string root, string outDir)
        {
            string target = Stylesheets.OutputPath(source);
            return outDir == null ? target : Path.Combine(Path.GetFullPath(outDir), Path.GetRelativePath(root, target));
        }

        private static IEnumerable<string> Find(string folder) =>
            Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(Stylesheets.IsStylesheet)
                .Where(f => !Path.GetFileName(f).StartsWith('_'))
                .Where(f => !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar).Any(part => part == "node_modules" || part == ".git"))
                .OrderBy(f => f, StringComparer.Ordinal);
    }
}
