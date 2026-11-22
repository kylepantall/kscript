using System;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// The file names of scripts. There is one extension per form: <c>.kscript</c> for the brace form and
    /// <c>.ks.xml</c> for the XML form (tests add <c>.test</c> before it). Every tool that looks for scripts
    /// (check, fmt, convert, test, the editor) asks here, so they all agree.
    /// </summary>
    public static class ScriptFiles
    {
        /// <summary>The extension of a script in the brace form.</summary>
        public const string Code = ".kscript";

        /// <summary>The extension of a script in the XML form.</summary>
        public const string Xml = ".ks.xml";

        /// <summary>Both script extensions, the default form first.</summary>
        public static readonly string[] Extensions = { Code, Xml };

        /// <summary>The endings of test files.</summary>
        public static readonly string[] TestExtensions = { ".test" + Code, ".test" + Xml };

        /// <summary>True for a <c>.kscript</c> file.</summary>
        public static bool IsCode(string path) => path != null && path.EndsWith(Code, StringComparison.OrdinalIgnoreCase);

        /// <summary>True for a <c>.ks.xml</c> file.</summary>
        public static bool IsXml(string path) => path != null && path.EndsWith(Xml, StringComparison.OrdinalIgnoreCase);

        /// <summary>True for a script in either form.</summary>
        public static bool IsScript(string path) => IsCode(path) || IsXml(path);

        /// <summary>True for a test file in either form.</summary>
        public static bool IsTest(string path) =>
            path != null && (path.EndsWith(TestExtensions[0], StringComparison.OrdinalIgnoreCase) || path.EndsWith(TestExtensions[1], StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// What to rename a file to when it has one of the old script extensions (<c>.ks</c>, or a plain <c>.xml</c>
        /// that holds a script), or null when the name is not one of those.
        /// </summary>
        public static string RenamedFrom(string path)
        {
            if (path == null || IsScript(path)) return null;
            if (path.EndsWith(".ks", StringComparison.OrdinalIgnoreCase)) return path + ".xml";
            if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return path.Substring(0, path.Length - 4) + Xml;
            return null;
        }
    }
}
