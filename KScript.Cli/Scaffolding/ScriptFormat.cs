using System;
using System.Collections.Generic;

using KScript.Projects;
using KScript.Syntax.Markup;

namespace KScript.Scaffolding
{
    /// <summary>
    /// The format <c>kscript make</c> writes scripts in: .kscript (the default) or XML (.ks.xml). Templates are written
    /// once, as .kscript; with <c>--format xml</c> (or <c>"make": { "format": "xml" }</c> in kscript.json) a .kscript
    /// template's output is converted to XML and its .kscript ending becomes .ks.xml. Templates refer to other scripts
    /// with <c>{{ ext }}</c> (".kscript" or ".ks.xml"), and <c>{{#if xml}}</c> tells the formats apart.
    /// </summary>
    public static class ScriptFormat
    {
        public const string KScript = MakeSettings.KScriptFormat;
        public const string Xml = MakeSettings.XmlFormat;

        /// <summary>--format if given, else the project's make.format, else .kscript.</summary>
        public static string Resolve(IReadOnlyDictionary<string, string> options, ProjectConfig project)
        {
            string format = options != null && options.TryGetValue("format", out var f) ? f : null;
            format = (format ?? project?.Make.Format ?? KScript).Trim().ToLowerInvariant();
            if (format == "ks" || format == "code") format = KScript;
            if (format == "ks.xml") format = Xml;
            if (format != KScript && format != Xml)
                throw new StubException($"--format should be kscript or xml, not '{format}'.");
            return format;
        }

        /// <summary>Sets format, ext, xml and kscript for templates.</summary>
        public static void AddValues(IDictionary<string, string> values, string format)
        {
            bool xml = format == Xml;
            values["format"] = format;
            values["ext"] = xml ? ".ks.xml" : ".kscript";
            values["xml"] = xml ? "true" : "false";
            values["kscript"] = xml ? "false" : "true";
        }

        /// <summary>
        /// A rendered file in the chosen format: a .kscript template's output is converted to XML when the format is
        /// xml, and a path ending in .kscript then ends in .ks.xml. Other files are left alone.
        /// </summary>
        public static (string Path, string Content) Apply(string templateFile, string path, string content, string format)
        {
            if (format != Xml || !MarkupParser.IsCodePath(templateFile)) return (path, content);
            // The parser and converter report problems as diagnostics, never by throwing.
            ConversionResult result = ScriptConverter.ToXml(CodeParser.Parse(content), nameList: global::KScript.Language.Catalog.LanguageCatalog.Default.IsNameList);
            if (MarkupParser.IsCodePath(path)) path = ScriptConverter.ConvertedPath(path);
            // Lay it out as kscript fmt would, so a new project passes kscript fmt --check.
            string text = global::KScript.Language.Formatting.Formatter.Format(result.Text, path)?.Text ?? result.Text;
            return (path, text);
        }
    }
}
