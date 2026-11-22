using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KScript.Projects
{
    /// <summary>A standard that was applied to a project, in the order it was applied.</summary>
    public sealed class AppliedStandard
    {
        public AppliedStandard(string label, string name, string description, string path, IReadOnlyList<string> extends)
        {
            Label = label;
            Name = name;
            Description = description;
            Path = path;
            Extends = extends;
        }

        /// <summary>How the standard is shown: kscript:recommended, or its path relative to the project.</summary>
        public string Label { get; }

        /// <summary>The "name" the file gives itself, or null.</summary>
        public string Name { get; }

        public string Description { get; }

        /// <summary>The standard file's full path, or null for a built-in standard.</summary>
        public string Path { get; }

        /// <summary>The standards it extends, as written.</summary>
        public IReadOnlyList<string> Extends { get; }

        public bool IsBuiltIn => Path == null;
    }

    /// <summary>
    /// Standards: named, shareable sets of lint rules and format settings. A project picks one (or several) with
    /// <c>"standard"</c> in kscript.json; a standard can <c>"extends"</c> others. Settings are applied in layers, and
    /// later layers win: the standards a standard extends, then the standard itself, then kscript.json's own settings.
    /// With no <c>"standard"</c>, a project uses <see cref="Recommended"/>.
    /// <code>
    /// // standards/acme.kscript-standard.json
    /// {
    ///   "name": "acme",
    ///   "extends": "kscript:recommended",
    ///   "lint": { "rules": { "KS6*": "error", "KS3003": "off" } },
    ///   "format": { "indent": 4, "print_width": 100 }
    /// }
    /// </code>
    /// </summary>
    public static class Standards
    {
        /// <summary>The standard a project uses when kscript.json doesn't name one.</summary>
        public const string Recommended = "kscript:recommended";

        public const string Strict = "kscript:strict";

        /// <summary>The prefix of built-in standards.</summary>
        public const string BuiltInPrefix = "kscript:";

        /// <summary>The file a standard folder holds, so <c>"standard": "../shared/acme"</c> can name the folder.</summary>
        public const string FileName = "kscript-standard.json";

        /// <summary>Lint codes that are opinions rather than problems (KS6xxx). They are off unless a standard turns them on.</summary>
        public static bool IsStyleRule(string code) =>
            code != null && code.StartsWith("KS6", StringComparison.OrdinalIgnoreCase);

        /// <summary>The built-in standards, by name, as the same JSON a standard file holds.</summary>
        public static readonly IReadOnlyDictionary<string, string> BuiltIn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Recommended] = """
                {
                  "name": "kscript:recommended",
                  "description": "The default. Style rules (KS6xxx, such as KS6001 early returns) are warnings, and files use the formatter's standard layout.",
                  "lint": {
                    "rules": { "KS6*": "warning" }
                  },
                  "format": {
                    "print_width": 80,
                    "max_blank_lines": 1,
                    "end_of_line": "auto",
                    "rules": {
                      "indent": true,
                      "statement_per_line": true,
                      "tidy_tags": true,
                      "wrap_attributes": true,
                      "trim_trailing_whitespace": true,
                      "collapse_blank_lines": true,
                      "final_newline": true,
                      "spacing": true,
                      "brace_style": true,
                      "wrap_strings": true,
                      "wrap_chains": true,
                      "wrap_bodies": true,
                      "wrap_comments": true
                    }
                  }
                }
                """,
            [Strict] = """
                {
                  "name": "kscript:strict",
                  "description": "kscript:recommended, with style rules as errors and warnings failing kscript check.",
                  "extends": "kscript:recommended",
                  "lint": {
                    "rules": { "KS6*": "error" },
                    "warnings_as_errors": true
                  }
                }
                """,
        };

        /// <summary>
        /// Finds the standard a reference names. Built-in names (kscript:recommended) are returned as written, in their
        /// canonical case. Anything else is a path relative to <paramref name="baseDirectory"/> (or starting with ~/):
        /// a standard file, or a folder holding <see cref="FileName"/>. Returns the full path of the file.
        /// </summary>
        public static string Resolve(string reference, string baseDirectory, Func<string, bool> fileExists = null)
        {
            fileExists ??= File.Exists;
            string r = reference?.Trim();
            if (string.IsNullOrEmpty(r)) throw new ProjectConfigException("A standard can't be an empty string.");

            if (r.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string known = BuiltIn.Keys.FirstOrDefault(k => string.Equals(k, r, StringComparison.OrdinalIgnoreCase));
                if (known == null)
                    throw new ProjectConfigException($"there is no built-in standard '{r}'. Built-in standards: {string.Join(", ", BuiltIn.Keys)}.");
                return known;
            }

            string path = r;
            if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
                path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 2 ? path.Substring(2) : string.Empty);

            string full;
            try
            {
                full = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory ?? ".", path));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new ProjectConfigException($"the standard '{r}' is not a valid path.");
            }

            if (fileExists(full)) return full;
            string inFolder = System.IO.Path.Combine(full, FileName);
            if (fileExists(inFolder)) return inFolder;
            throw new ProjectConfigException($"can't find the standard '{r}' (looked for {full} and {inFolder}).");
        }

        public static bool IsBuiltInName(string resolved) => resolved != null && BuiltIn.ContainsKey(resolved);
    }
}
