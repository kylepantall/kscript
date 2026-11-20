using KScript.Http;
using KScript.Projects;

namespace KScript.Language.Outputs
{
    /// <summary>
    /// The OpenAPI document for a site's routes, kept in the file <c>http.openapi.file</c> names in kscript.json
    /// (<c>openapi.json</c>, or <c>openapi.yaml</c> for YAML). Off until that is set.
    /// </summary>
    public sealed class OpenApiOutput : IProjectOutput
    {
        // The files this output has been told about, relative to their root, so a watcher ignores its own writes.
        private readonly HashSet<string> files = new HashSet<string>(StringComparer.Ordinal);

        public string Name => "openapi";

        public bool IsEnabled(string root)
        {
            try
            {
                return FileFor(root, out _) != null;
            }
            catch (ProjectConfigException)
            {
                // kscript.json can't be read: the command that reads it next says why.
                return false;
            }
        }

        public IReadOnlyList<string> Write(string root, bool check)
        {
            string relative = FileFor(root, out ProjectConfig project);
            if (relative == null) return Array.Empty<string>();
            var app = new HttpApp(project.SiteRoot, project);
            if (!app.HasRoutes) return Array.Empty<string>();

            Dictionary<string, object> document = OpenApi.Document(app, app.LoadRoutes(), project);
            bool yaml = relative.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
            // \n line ends on every system, so the file doesn't change when someone else rebuilds it.
            string text = (yaml ? OpenApi.ToYaml(document) : OpenApi.ToJson(document)).Replace("\r\n", "\n");
            string path = Path.Combine(root, relative);
            if (File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == text) return Array.Empty<string>();
            if (!check)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text);
            }
            return new[] { relative };
        }

        public bool Owns(string relative)
        {
            lock (files) return files.Contains(relative.Replace('\\', '/'));
        }

        /// <summary>The document's file relative to <paramref name="root"/> (/ separators), or null when kscript.json names none.</summary>
        private string FileFor(string root, out ProjectConfig project)
        {
            project = null;
            if (root == null) return null;
            string config = Path.Combine(root, ProjectConfig.FileName);
            if (!File.Exists(config)) return null;
            project = ProjectConfig.Load(config);
            string file = project.Http.OpenApi.File;
            if (string.IsNullOrWhiteSpace(file)) return null;
            string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(Path.Combine(root, file))).Replace('\\', '/');
            lock (files) files.Add(relative);
            return relative;
        }
    }
}
