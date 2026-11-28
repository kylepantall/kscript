using System.Diagnostics;

namespace KScript.Testing
{
    /// <summary>The files git sees as changed in a folder, with their text before the change, for <c>kscript test --changed</c>.</summary>
    public static class GitChanges
    {
        /// <summary>
        /// The files under <paramref name="folder"/> that differ from <paramref name="baseRef"/> (HEAD when null: the
        /// changes not committed yet), untracked files included, each relative to the folder with its text at the base
        /// (null for a file that is new). Throws <see cref="InvalidOperationException"/> when git can't say.
        /// </summary>
        public static Dictionary<string, string> Since(string folder, string baseRef = null)
        {
            string at = string.IsNullOrEmpty(baseRef) ? "HEAD" : baseRef;
            Git(folder, out _, "rev-parse", "--verify", "--quiet", at + "^{commit}");
            Git(folder, out string diff, "diff", "--name-only", "--relative", "--no-renames", at, "--", ".");
            Git(folder, out string untracked, "ls-files", "--others", "--exclude-standard", "--", ".");

            var changes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in Lines(diff))
            {
                // A file the base doesn't have is new; git show fails for it.
                changes[file] = TryGit(folder, out string text, "show", $"{at}:./{file}") ? text : null;
            }
            foreach (string file in Lines(untracked)) changes[file] = null;
            return changes;
        }

        private static IEnumerable<string> Lines(string text) =>
            text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static void Git(string folder, out string output, params string[] args)
        {
            if (!TryGit(folder, out output, args, out string error))
            {
                string what = args[0] == "rev-parse" ? $"git doesn't know {args[^1].Replace("^{commit}", string.Empty)}" : error.Trim();
                throw new InvalidOperationException(string.IsNullOrEmpty(what) ? "git couldn't list the changes." : what);
            }
        }

        private static bool TryGit(string folder, out string output, params string[] args) => TryGit(folder, out output, args, out _);

        private static bool TryGit(string folder, out string output, string[] args, out string error)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = folder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            try
            {
                using Process git = Process.Start(start);
                Task<string> err = git.StandardError.ReadToEndAsync();
                output = git.StandardOutput.ReadToEnd();
                git.WaitForExit();
                error = err.GetAwaiter().GetResult();
                return git.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                output = string.Empty;
                error = "git isn't installed, or isn't on the PATH.";
                return false;
            }
        }
    }
}
