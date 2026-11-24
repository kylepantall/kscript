using KScript.Projects;

namespace KScript.Language.Agents
{
    public static partial class AgentDocs
    {
        private const string LegacyBegin = "<!-- kscript agents: begin -->";
        private const string BeginPrefix = "<!-- kscript agents: begin.";
        private static StringComparer AgentPathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private static List<(string Path, string Text)> ReadMarkdown(string root, ProjectConfig config)
        {
            var included = new List<(string Path, string Text)>();
            var seen = new HashSet<string>(AgentPathComparer);
            foreach (string configured in config?.AgentMarkdown ?? Array.Empty<string>())
            {
                string path = configured.Replace('\\', '/');
                string prefix = $"{config.FilePath}: agents.merge entry '{configured}'";
                if (Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\0')
                    || path.Split('/').Contains("..")
                    || !(path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)))
                    throw new ProjectConfigException($"{prefix} must be a relative Markdown file inside the project.");

                string file = Path.GetFullPath(Path.Combine(root, path));
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Equals(AgentsFile, StringComparison.OrdinalIgnoreCase)
                    || relative.Equals("CLAUDE.md", StringComparison.OrdinalIgnoreCase)
                    || relative.StartsWith(ProjectConfig.GeneratedFolder + "/", StringComparison.OrdinalIgnoreCase))
                    throw new ProjectConfigException($"{prefix} cannot include an agent output file.");
                if (!seen.Add(file)) continue;

                try
                {
                    string current = root;
                    foreach (string part in relative.Split('/'))
                    {
                        current = Path.Combine(current, part);
                        if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                            throw new ProjectConfigException($"{prefix} must not go through a symbolic link.");
                    }

                    string text = File.ReadAllText(file).Replace("\r\n", "\n");
                    if (MarkerLines(text, out bool unclosedFence).Count > 0)
                        throw new ProjectConfigException($"{prefix} contains a reserved AGENTS.md marker line.");
                    if (unclosedFence)
                        throw new ProjectConfigException($"{prefix} has an unclosed Markdown code fence.");
                    included.Add((file, text));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    throw new ProjectConfigException($"{prefix} could not be read: {error.Message}");
                }
            }
            return included;
        }

        /// <summary>
        /// Replaces the generated section, preserving handwritten Markdown. Without markers, appends the section.
        /// A misplaced or duplicate marker is an error so an update cannot remove handwritten text.
        /// </summary>
        public static string Merge(string existing, string section)
        {
            if (string.IsNullOrEmpty(existing)) return section;

            int firstLf = existing.IndexOf('\n');
            string newline = firstLf > 0 && existing[firstLf - 1] == '\r' ? "\r\n" : "\n";
            var markers = MarkerLines(existing, out bool unclosedFence);
            if (markers.Count == 0)
            {
                if (unclosedFence)
                    throw new ProjectConfigException("Close the Markdown code fence in AGENTS.md before appending a generated section.");

                string separator = existing.EndsWith(newline + newline, StringComparison.Ordinal) ? string.Empty
                    : existing.EndsWith('\n') ? newline : newline + newline;
                return existing + separator + MatchNewlines(section, newline);
            }
            if (markers.Count != 2 || !markers[0].Begin || markers[1].Begin)
                throw new ProjectConfigException("AGENTS.md needs one matching begin and end marker on their own lines. Fix the markers and run kscript agents again.");

            var begin = markers[0];
            var end = markers[1];
            if (begin.After > begin.Start && existing[begin.After - 1] == '\n')
                newline = begin.After > begin.Start + 1 && existing[begin.After - 2] == '\r' ? "\r\n" : "\n";
            return existing[..begin.Start] + MatchNewlines(section, newline) + existing[end.After..];
        }

        private static string MatchNewlines(string text, string newline) => text.Replace("\r\n", "\n").Replace("\n", newline);

        private static List<(int Start, int After, bool Begin)> MarkerLines(string text, out bool unclosedFence)
        {
            var markers = new List<(int Start, int After, bool Begin)>();
            char fence = '\0';
            int width = 0;
            for (int start = 0; start < text.Length;)
            {
                int end = text.IndexOf('\n', start);
                if (end < 0) end = text.Length;
                ReadOnlySpan<char> line = text.AsSpan(start, end - start).TrimEnd();
                int after = Math.Min(end + 1, text.Length);

                ReadOnlySpan<char> trimmed = line.TrimStart(' ');
                if (line.Length - trimmed.Length <= 3 && trimmed.Length >= 3 && trimmed[0] is '`' or '~')
                {
                    char symbol = trimmed[0];
                    int count = 1;
                    while (count < trimmed.Length && trimmed[count] == symbol) count++;
                    if (count >= 3 && fence == '\0' && (symbol != '`' || !trimmed[count..].Contains('`')))
                        (fence, width) = (symbol, count);
                    else if (symbol == fence && count >= width && trimmed[count..].IsEmpty)
                        (fence, width) = ('\0', 0);
                }
                if (fence == '\0')
                {
                    if (line.SequenceEqual(End)) markers.Add((start, after, false));
                    else if (line.SequenceEqual(LegacyBegin)
                        || line.StartsWith(BeginPrefix, StringComparison.Ordinal) && line.EndsWith("-->", StringComparison.Ordinal))
                        markers.Add((start, after, true));
                }
                start = after;
            }
            unclosedFence = fence != '\0';
            return markers;
        }
    }
}
