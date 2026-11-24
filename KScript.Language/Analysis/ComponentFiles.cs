using KScript.Projects;
using KScript.Syntax.Markup;

namespace KScript.Language.Analysis
{
    internal static class ComponentFiles
    {
        internal static string Folder(string path, IFileSystem files)
        {
            if (path == null) return null;
            string nearest = null;
            for (string dir = Path.GetDirectoryName(Path.GetFullPath(path)); dir != null; dir = Path.GetDirectoryName(dir))
            {
                string conventional = Path.Combine(dir, "_components");
                if (nearest == null && files.DirectoryExists(conventional)) nearest = conventional;
                string configPath = Path.Combine(dir, ProjectConfig.FileName);
                if (files.ReadText(configPath) is not string text) continue;
                try
                {
                    var project = ProjectConfig.Parse(text, configPath);
                    string configured = Path.GetFullPath(Path.Combine(project.SiteRoot, project.PathFor("components")));
                    return files.DirectoryExists(configured) ? configured : nearest;
                }
                catch (ProjectConfigException)
                {
                    return nearest;
                }
            }
            return nearest;
        }

        internal static IEnumerable<string> List(string dir, IFileSystem files) =>
            ComponentScript.Extensions.SelectMany(ext => files.ListFiles(dir, "*" + ext))
                .Select(ComponentScript.Tag).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(tag => ComponentScript.FindFile(dir, tag, files.FileExists, files.ReadText))
                .Where(path => path != null);
    }
}
