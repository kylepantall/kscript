namespace KScript
{
    /// <summary>
    /// What the kscript command's exit code means, the same for every command, so CI can tell "the checks failed"
    /// from "the command was wrong". docs/projects.md and kscript --help list them.
    /// </summary>
    public static class ExitCodes
    {
        /// <summary>Everything worked.</summary>
        public const int Ok = 0;

        /// <summary>
        /// The command ran and found problems: checks or tests failed, a <c>--check</c> found something out of date,
        /// or the work could not be finished (a download, a port that is in use).
        /// </summary>
        public const int Failed = 1;

        /// <summary>
        /// The command could not be run as asked: an unknown command or option, a missing or wrong value, a file or
        /// folder that is not there, or a kscript.json (or standard) that can't be used.
        /// </summary>
        public const int Usage = 2;

        /// <summary>A script stopped with an error.</summary>
        public const int ScriptError = 3;

        /// <summary>An error inside kscript itself.</summary>
        public const int Internal = 70;

        /// <summary>Stopped with Ctrl+C.</summary>
        public const int Interrupted = 130;

        /// <summary>The codes with their meanings, in order, for kscript --help and the docs.</summary>
        public static readonly (int Code, string Meaning)[] All =
        {
            (Ok, "Everything worked."),
            (Failed, "The command ran and found problems: checks or tests failed, or a --check found something out of date."),
            (Usage, "The command line or the settings are wrong: an unknown command or option, a missing file, or a kscript.json that can't be used."),
            (ScriptError, "A script stopped with an error."),
            (Internal, "An error inside kscript itself."),
            (Interrupted, "Stopped with Ctrl+C. (serve and watch run until they are stopped, so they end with 0.)"),
        };
    }
}
