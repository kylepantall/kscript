using System;
using System.IO;
using System.Linq;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Cli
{
    public static class RepoCommand
    {
        public static int Run(string[] args, TextWriter output, TextWriter error) =>
            Run(args, output, error, WorkingFolder.Current);

        internal static int Run(string[] args, TextWriter output, TextWriter error, string workingDirectory)
        {
            if (args.SequenceEqual(new[] { "add", "--help" }) || args.SequenceEqual(new[] { "add", "-h" }))
            {
                CliReference.PrintCommand(output, CliReference.Find("repo"));
                return ExitCodes.Ok;
            }
            if (args.Length != 2 || args[0] != "add" || !LibCommand.TryParseRepository(args[1], out string repository))
            {
                error.WriteLine("Usage: kscript repo add <owner/repo | GitHub repository URL>");
                return ExitCodes.Usage;
            }
            try
            {
                string root = ProjectConfig.FindAndLoad(workingDirectory)?.Directory ?? workingDirectory;
                bool added = LibraryRepositories.Add(root, repository);
                output.WriteLine(added ? $"Registered {repository} in {root}." : $"{repository} is already available in {root}.");
                return ExitCodes.Ok;
            }
            catch (ProjectConfigException ex) { error.WriteLine(ex.Message); return ExitCodes.Usage; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"Could not register {repository}: {ex.Message}");
                return ExitCodes.Failed;
            }
        }
    }
}
