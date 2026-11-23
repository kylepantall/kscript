using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KScript.Cli
{
    internal static class LibraryRepositories
    {
        internal const string DefaultRepository = "kylepantall/kscript";
        internal static IReadOnlyList<string> PublisherOwners { get; } = new[] { "orimovo", "waggr-gb" };

        internal static bool IsPublisherRepository(string repository)
        {
            int slash = repository.IndexOf('/');
            return slash > 0 && slash < repository.Length - 1 && slash == repository.LastIndexOf('/')
                && PublisherOwners.Contains(repository[..slash]);
        }

        internal static bool Contains(string root, string repository) =>
            repository == DefaultRepository || IsPublisherRepository(repository) || File.Exists(Registration(root, repository));

        internal static IEnumerable<string> Registered(string root)
        {
            string folder = Path.Combine(root, ".kscript", "repos");
            LibCommand.CheckDestination(root, folder);
            if (!Directory.Exists(folder)) yield break;
            foreach (string file in Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName, System.StringComparer.Ordinal))
            {
                string marker = Path.GetFileName(file);
                if (marker.Count(c => c == '@') != 1 || new FileInfo(file).LinkTarget != null) continue;
                string name = marker.Replace('@', '/');
                if (LibCommand.TryParseRepository(name, out var repository) && repository == name) yield return repository;
            }
        }

        internal static bool Add(string root, string repository)
        {
            if (repository == DefaultRepository || IsPublisherRepository(repository)) return false;
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
