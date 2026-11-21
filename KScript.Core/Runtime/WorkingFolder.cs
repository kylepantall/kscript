namespace KScript.Runtime
{
    /// <summary>
    /// The folder kscript is working in. Unlike <see cref="Directory.GetCurrentDirectory"/>, it copes with that folder
    /// being deleted or replaced while kscript runs (an editor or a build that recreates the project folder, say): it
    /// goes back to the same path when a folder is there again, and otherwise throws a KScript error that says what
    /// happened instead of a .NET exception.
    /// </summary>
    public static class WorkingFolder
    {
        private static string remembered;

        /// <summary>The folder noted by <see cref="Remember"/> (or the first time <see cref="Current"/> was read).</summary>
        internal static string Remembered => remembered;

        /// <summary>
        /// Notes the folder kscript started in, so <see cref="Current"/> can go back to it. The CLI calls this first
        /// thing. When the folder is already gone, the shell's PWD is used.
        /// </summary>
        public static void Remember()
        {
            try { remembered = Directory.GetCurrentDirectory(); }
            catch (Exception ex) when (IsMissingFolder(ex)) { remembered = Environment.GetEnvironmentVariable("PWD"); }
        }

        /// <summary>The working folder's full path. See the class summary for what happens when it no longer exists.</summary>
        public static string Current
        {
            get
            {
                try
                {
                    string current = Directory.GetCurrentDirectory();
                    remembered ??= current;
                    return current;
                }
                catch (Exception ex) when (IsMissingFolder(ex))
                {
                    return Recover(ex);
                }
            }
        }

        /// <summary>
        /// Makes sure the working folder still exists, going back to the remembered path when it was replaced.
        /// kscript serve calls this before each request, so a recreated project folder keeps being served.
        /// </summary>
        public static void Check() => _ = Current;

        private static string Recover(Exception ex)
        {
            string path = remembered;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Directory.SetCurrentDirectory(path);
                return path;
            }
            throw Missing(path, ex);
        }

        /// <summary>The error for a working folder that no longer exists.</summary>
        internal static KScriptException Missing(string path, Exception inner = null) => new KScriptException(
            (string.IsNullOrEmpty(path) ? "The folder kscript was started in" : $"The folder kscript was started in ({path})")
            + " no longer exists: it was deleted, moved or replaced. Go to a folder that exists (cd) and run the command again.",
            "FileNotFound", inner);

        /// <summary>True for the exceptions .NET throws when the working folder is gone.</summary>
        internal static bool IsMissingFolder(Exception ex) => ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is IOException || ex is UnauthorizedAccessException;
    }
}
