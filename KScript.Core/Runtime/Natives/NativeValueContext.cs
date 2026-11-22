namespace KScript.Runtime
{
    /// <summary>Where a native type's value is being written, for <see cref="NativeType.Suggest"/>.</summary>
    public sealed class NativeValueContext
    {
        /// <summary>The full path of the file being edited, or null for an unsaved one.</summary>
        public string DocumentPath { get; init; }

        /// <summary>The folder holding the nearest kscript.json, or the file's own folder when there is none.</summary>
        public string ProjectRoot { get; init; }

        /// <summary>What has been typed of the value so far (without quotes): "/au".</summary>
        public string Typed { get; init; } = string.Empty;

        /// <summary>The options written with the type, by name: path(exists: route) gives exists = "route".</summary>
        public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();

        /// <summary>The project's routes, worked out only when asked for: method, path and the handler, as written.</summary>
        public Func<IReadOnlyList<(string Method, string Path, string Handler)>> Routes { get; init; } = () => Array.Empty<(string, string, string)>();

        /// <summary>Reads files and folders (the editor's own copy of open files first), so suggestions can list what exists.</summary>
        public Func<string, IEnumerable<string>> ListFolder { get; init; } = _ => Array.Empty<string>();
    }

    /// <summary>One value the editor suggests for a native type, with an optional short detail and Markdown docs.</summary>
    public sealed class NativeValueSuggestion
    {
        public NativeValueSuggestion(string value, string detail = null, string documentation = null)
        {
            Value = value;
            Detail = detail;
            Documentation = documentation;
        }

        /// <summary>The value as it is written, without quotes: Europe/London, /login.</summary>
        public string Value { get; }

        /// <summary>A few words shown beside it: "GET → accounts.show_login".</summary>
        public string Detail { get; }

        public string Documentation { get; }
    }
}
