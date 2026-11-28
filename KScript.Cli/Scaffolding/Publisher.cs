using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KScript.Language.Formatting;
using KScript.Projects;
using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Scaffolding
{
    /// <summary>
    /// <c>kscript publish</c>: copies a model a package ships (KScript.Auth's User, Guest and Ownable) into the
    /// project's models folder, so the project can change it. The copy replaces the package's model wherever the
    /// package is imported, at runtime and in the editor.
    /// </summary>
    public static class Publisher
    {
        /// <summary>A model a package ships that a project can publish.</summary>
        public sealed record Publishable(PackageSource Source, string Model, string Kind, string Summary);

        /// <summary>Every model and mixin the installed packages ship, by package and then name.</summary>
        public static IReadOnlyList<Publishable> All()
        {
            var list = new List<Publishable>();
            foreach (KScriptPackage package in PackageRegistry.Default.LoadAll())
            {
                foreach (PackageSource source in package.Sources)
                {
                    MarkupElement model = CodeParser.Parse(source.Text).RootElements.FirstOrDefault()?.ChildElements
                        .FirstOrDefault(e => e.Name == "model" && e.GetAttributeValue("name") != null);
                    if (model == null) continue;
                    string summary = DocComment.Parse(model.Documentation)?.Summary?.Trim() ?? string.Empty;
                    string first = summary.Split(new[] { ". " }, 2, StringSplitOptions.None)[0].TrimEnd('.');
                    bool mixin = model.Attributes.Any(a => a.Name == "mixin") || (model.GetAttributeValue("options") ?? string.Empty).Contains("mixin", StringComparison.Ordinal)
                        || source.Text.Contains("model " + model.GetAttributeValue("name").Trim() + "(mixin", StringComparison.Ordinal);
                    list.Add(new Publishable(source, model.GetAttributeValue("name").Trim(), mixin ? "mixin" : "model", first.Length > 0 ? first + "." : string.Empty));
                }
            }
            return list.OrderBy(p => p.Source.Package, StringComparer.Ordinal).ThenBy(p => p.Model, StringComparer.Ordinal).ToList();
        }

        /// <summary>The publishable model called <paramref name="name"/> (any case), or an error that lists them.</summary>
        public static Publishable Find(string name)
        {
            IReadOnlyList<Publishable> all = All();
            Publishable found = all.FirstOrDefault(p => string.Equals(p.Model, name, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
            string close = Values.Closest(name, all.Select(p => p.Model));
            throw new StubException($"No package ships a model called '{name}'." + (close != null ? $" Did you mean {close}?" : string.Empty)
                + $" kscript publish lists them: {string.Join(", ", all.Select(p => p.Model))}.");
        }

        /// <summary>
        /// Where the project's copy of a model goes, relative to the project folder: models/user.kscript, or .ks.xml
        /// in the XML syntax.
        /// </summary>
        public static string PathFor(ProjectConfig project, Publishable item, string format)
        {
            string folder = project != null ? Path.GetRelativePath(project.Directory, project.PathFor("models")).Replace('\\', '/') : "models";
            return folder.TrimEnd('/') + "/" + item.Source.Name + (format == ScriptFormat.Xml ? ".ks.xml" : ".kscript");
        }

        /// <summary>The project's copy of a model the project already has, relative to the project folder, or null.</summary>
        public static string Existing(string root, ProjectConfig project, Publishable item)
        {
            string copy = project != null ? item.Source.ProjectCopy(project.ModelFiles()) : null;
            return copy != null ? Path.GetRelativePath(root, copy).Replace('\\', '/') : null;
        }

        /// <summary>
        /// The text of a copy: the package's script in the chosen syntax, laid out as kscript fmt would, with
        /// <paramref name="change"/> applied to the .kscript text first (kscript make auth --passkeys makes the
        /// password optional).
        /// </summary>
        public static string Text(Publishable item, string path, string format, FormatOptions options = null, Func<string, string> change = null)
        {
            string text = change != null ? change(item.Source.Text) : item.Source.Text;
            return ScriptFormat.Apply(item.Source.FileName, path, text, format, options).Content;
        }

        /// <summary>
        /// Writes the project's copy of a model (unless <paramref name="dryRun"/>). Returns where it went, relative to
        /// the project folder. A copy the project already has is kept unless <paramref name="force"/>.
        /// </summary>
        public static string Publish(string root, Publishable item, IReadOnlyDictionary<string, string> options, bool force, bool dryRun, Func<string, string> change = null)
        {
            root = Path.GetFullPath(root);
            ProjectConfig project = ProjectConfig.FindAndLoad(root);
            string format = ScriptFormat.Resolve(options, project);
            string existing = Existing(root, project, item);
            if (existing != null && !force)
                throw new StubException($"This project already has its own {item.Model} in {existing}. Use --force to replace it with {item.Source.Package}'s.");
            string rel = existing ?? PathFor(project, item, format);
            if (existing != null) format = MarkupParser.IsCodePath(existing) ? ScriptFormat.KScript : ScriptFormat.Xml;
            string full = Path.Combine(root, rel);
            string text = Text(item, full, format, FormatOptions.From(project), change);
            if (!dryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
            }
            return rel;
        }
    }
}
