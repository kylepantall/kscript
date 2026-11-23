using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using KScript.Projects;

namespace KScript.Cli;

internal static class ProjectLibraries
{
    internal static void Record(string root, LibraryDependency dependency)
    {
        dependency.Validate();
        string file = ProjectConfig.Find(root) ?? Path.GetFullPath(Path.Combine(root, ProjectConfig.FileName));
        string directory = Path.GetDirectoryName(file);
        LibCommand.CheckDestination(directory, file);

        string lockFile = Path.Combine(directory, ".kscript", "libraries.lock");
        LibCommand.CheckDestination(directory, lockFile);
        Directory.CreateDirectory(Path.GetDirectoryName(lockFile));
        using var writeLock = Acquire(lockFile);
        RecordUnderLock(file, dependency);
    }

    // Leave the empty file in place. Deleting it while another process is opening it
    // could give the next installer a different inode to lock. The OS releases the
    // exclusive handle even when an installer exits before its atomic rename.
    private static FileStream Acquire(string lockFile)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (File.Exists(lockFile) && timer.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(20);
            }
        }
    }

    private static void RecordUnderLock(string file, LibraryDependency dependency)
    {
        bool exists = File.Exists(file);
        Encoding encoding = new UTF8Encoding(false);
        string json = "{}" + Environment.NewLine;
        if (exists)
        {
            using var reader = new StreamReader(file, encoding, detectEncodingFromByteOrderMarks: true);
            json = reader.ReadToEnd();
            encoding = reader.CurrentEncoding;
        }
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var installed = LibraryDependency.ReadAll(document.RootElement);
            if (installed.TryGetValue(dependency.Name, out var prior))
            {
                if (prior == dependency) return;
                throw new ProjectConfigException($"{file} already records '{prior.Name}' from {prior.Source} at {prior.Commit}. Resolve the conflicting dependency before adding '{dependency.Name}'.");
            }
        }
        catch (JsonException ex)
        {
            throw new ProjectConfigException($"{file}: invalid JSON: {ex.Message}");
        }

        string value = "{ \"source\": " + JsonConfigEdit.Quote(dependency.Source) + ", \"commit\": " + JsonConfigEdit.Quote(dependency.Commit) + " }";
        json = JsonConfigEdit.SetProperty(json, new[] { "libraries", dependency.Name }, value);
        string temporary = Path.Combine(Path.GetDirectoryName(file), ".kscript-libraries-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (exists && !OperatingSystem.IsWindows()) options.UnixCreateMode = File.GetUnixFileMode(file);
            using (var stream = new FileStream(temporary, options))
            using (var writer = new StreamWriter(stream, encoding))
                writer.Write(json);
            // UnixCreateMode is filtered by the installer's umask. Retain the original mode
            // on shared projects too, after the content has been fully written.
            if (!OperatingSystem.IsWindows() && options.UnixCreateMode is UnixFileMode mode)
                File.SetUnixFileMode(temporary, mode);
            if (exists) File.Replace(temporary, file, null);
            else File.Move(temporary, file);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
