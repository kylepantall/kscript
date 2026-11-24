using System;
using System.IO;
using System.Linq;

using KScript.Syntax;

namespace KScript.Pages
{
    /// <summary>Naming rules shared by page rendering and the language tools.</summary>
    public static class TemplateContract
    {
        public static string VariableName(string attribute) => attribute.Replace('-', '_').ToLowerInvariant();

        public static bool IsPropName(string name) => !string.IsNullOrEmpty(name) &&
            Lexer.IsIdentStart(name[0]) && name.Skip(1).All(c => Lexer.IsIdentChar(c) || c == '-');

        public static bool IsRequired(bool present, string value) =>
            present && (value == null || value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase));

        public static bool IsLoadCallback(string name) =>
            name.Equals("onLoad", StringComparison.OrdinalIgnoreCase) || name.Equals("on-load", StringComparison.OrdinalIgnoreCase);

        public static string CompanionPath(string template) => template != null &&
            string.Equals(Path.GetFileName(Path.GetDirectoryName(template)), "_components", StringComparison.OrdinalIgnoreCase)
                ? Path.ChangeExtension(template, ".kscript")
                : null;
    }
}
