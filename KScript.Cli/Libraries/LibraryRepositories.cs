using System.IO;

namespace KScript.Cli
{
    internal static class LibraryRepositories
    {
        internal const string DefaultRepository = "kylepantall/kscript";

        internal static bool Contains(string root, string repository) =>
            repository == DefaultRepository || File.Exists(Registration(root, repository));

        internal static bool Add(string root, string repository)
        {
            if (repository == DefaultRepository) return false;
            string registration = Registration(root, repository);
            if (File.Exists(registration)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(registration));
            try
            {
                // Each repository has its own marker, so two processes can add sources independently.
                using var marker = new FileStream(registration, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                return true;
            }
            catch (IOException) when (File.Exists(registration)) { return false; }
        }

        private static string Registration(string root, string repository)
        {
            string path = Path.Combine(root, ".kscript", "repos", repository.Replace('/', '@'));
            LibCommand.CheckDestination(root, path);
            return path;
        }
    }
}
