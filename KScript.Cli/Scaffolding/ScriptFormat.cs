using System;
using System.Collections.Generic;

using KScript.Language.Formatting;
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
        /// xml, and a path ending in .kscript then ends in .ks.xml. Scripts are then laid out as kscript fmt
        /// would (with the project's format settings, when given), so a new file passes kscript fmt --check whatever
        /// the names put into it. Other files are left alone.
        /// </summary>
        public static (string Path, string Content) Apply(string templateFile, string path, string content, string format, FormatOptions options = null)
        {
            if (format == Xml && MarkupParser.IsCodePath(templateFile))
            {
                // The parser and converter report problems as diagnostics, never by throwing.
                ConversionResult result = ScriptConverter.ToXml(CodeParser.Parse(content), nameList: global::KScript.Language.Catalog.LanguageCatalog.Default.IsNameList);
                if (MarkupParser.IsCodePath(path)) path = ScriptConverter.ConvertedPath(path);
                content = result.Text;
            }
            return (path, Tidy(path, content, options));
        }

        /// <summary>A script as kscript fmt lays it out; anything else, or a script it can't format, as it is.</summary>
        public static string Tidy(string path, string content, FormatOptions options = null)
        {
            string name = path.ToLowerInvariant();
            bool script = name.EndsWith(".kscript", StringComparison.Ordinal) || name.EndsWith(".ks.xml", StringComparison.Ordinal) || name.EndsWith(".ks", StringComparison.Ordinal);
            if (!script) return content;
            FormatResult formatted = Formatter.Format(content, path, options);
            return formatted.Succeeded ? formatted.Text : content;
        }
    }
}
