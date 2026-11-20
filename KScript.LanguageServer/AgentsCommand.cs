using KScript.Language.Agents;
using KScript.Projects;

namespace KScript.LanguageServer
{
    /// <summary>
    /// <c>kscript agents [folder] [--check] [--print]</c>: writes AGENTS.md and .kscript/agents for coding agents, from
    /// the project's code (signatures, types, enums, calls, errors thrown, doc comments) and the packages it imports.
    /// <c>--check</c> writes nothing and fails when the files are out of date, for CI; <c>--print</c> prints AGENTS.md's
    /// generated section instead of writing files.
    /// </summary>
    public static class AgentsCommand
    {
        public const string Usage = "Usage: kscript agents [folder] [--check] [--print]";

        /// <summary>Returns 0 when done (or up to date), 1 when --check finds stale files, 2 for bad usage.</summary>
        public static int Run(IEnumerable<string> args, TextWriter output)
        {
            bool check = false, print = false;
            string folder = null;
            foreach (string arg in args)
            {
                switch (arg)
                {
                    case "--check":
                        check = true;
                        break;
                    case "--print":
                        print = true;
                        break;
                    case "--help":
                    case "-h":
                        output.WriteLine(Usage);
                        return 0;
                    default:
                        if (arg.StartsWith("-", StringComparison.Ordinal) || folder != null)
                        {
                            output.WriteLine(Usage);
                            return 2;
                        }
                        folder = arg;
                        break;
                }
            }

            folder ??= Directory.GetCurrentDirectory();
            if (!Directory.Exists(folder) && !File.Exists(folder))
            {
                output.WriteLine($"{folder}: folder not found");
                return 2;
            }

            string root = AgentDocs.FindRoot(folder);
            try
            {
                if (print)
                {
                    output.Write(AgentDocs.AgentsSection(AgentDocs.Read(root)));
                    return 0;
                }

                IReadOnlyList<string> changed = AgentDocs.Write(root, check);
                string where = Path.GetRelativePath(Directory.GetCurrentDirectory(), root);
                where = where == "." ? string.Empty : $" in {where}";
                if (check)
                {
                    if (changed.Count == 0)
                    {
                        output.WriteLine($"Agent docs{where} are up to date.");
                        return 0;
                    }
                    foreach (string file in changed) output.WriteLine($"{file}: out of date");
                    output.WriteLine("Run kscript agents to update them.");
                    return 1;
                }

                foreach (string file in changed) output.WriteLine($"wrote {file}");
                int tokens = AgentDocs.EstimateTokens(File.ReadAllText(Path.Combine(root, AgentDocs.AgentsFile)));
                output.WriteLine(changed.Count == 0 ? $"Agent docs{where} are up to date." : $"Agent docs{where} updated: AGENTS.md is about {tokens:N0} tokens. The editor keeps .kscript/agents up to date on save.");
                return 0;
            }
            catch (ProjectConfigException ex)
            {
                output.WriteLine(ex.Message);
                return 2;
            }
        }
    }
}
