using System;
using System.Collections.Generic;

namespace KScript
{
    /// <summary>
    /// The spelling rules of kscript's command line, and the flags that were renamed. One flag has one meaning on
    /// every command: <c>--output text|json|yaml</c> is how results are printed, and <c>--syntax kscript|xml</c> is
    /// which script syntax is written or shown. An old flag is an error that says what to write, never an alias.
    /// </summary>
    public static class CliFlags
    {
        /// <summary>How results are printed: text, json or yaml.</summary>
        public const string Output = "--output";

        /// <summary>Which script syntax: kscript (.kscript) or xml (.ks.xml).</summary>
        public const string Syntax = "--syntax";

        /// <summary>Old flags by command, each with what to write instead.</summary>
        public static readonly IReadOnlyDictionary<(string Command, string Flag), string> RetiredFlags = new Dictionary<(string, string), string>
        {
            [("check", "--format")] = "--output text|json",
            [("check", "--warnaserror")] = "--warnings-as-errors",
            [("queue", "--format")] = "--output text|json",
            [("db", "--format")] = "--output text|json for db status, or --syntax kscript|xml for db diff",
            [("openapi", "--yaml")] = "--output yaml",
            [("openapi", "--json")] = "--output json",
            [("make", "--format")] = "--syntax kscript|xml",
            [("docs", "--format")] = "--syntax kscript|xml",
            [("mcp", "--format")] = "--syntax kscript|xml",
            [("convert", "--to")] = "--syntax kscript|xml",
        };

        /// <summary>
        /// The message for an old flag of a command (also in its <c>--flag=value</c> form), or null when the argument
        /// is not one.
        /// </summary>
        public static string Retired(string command, string argument)
        {
            if (argument == null || !argument.StartsWith("-", StringComparison.Ordinal)) return null;
            int eq = argument.IndexOf('=');
            string flag = eq > 0 ? argument.Substring(0, eq) : argument;
            return RetiredFlags.TryGetValue((command, flag), out string now)
                ? $"kscript {command}: {flag} was renamed. Use {now}."
                : null;
        }
    }
}
