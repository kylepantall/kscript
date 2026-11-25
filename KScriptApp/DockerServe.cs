using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

using KScript.Output;
using KScript.Projects;

namespace KScript;

internal static class DockerServe
{
    internal const string ContainerMarker = "KSCRIPT_SERVE_IN_DOCKER";
    internal const string MigrateSetting = "KSCRIPT_SERVE_MIGRATE";
    internal const string ReloadSetting = "KSCRIPT_LIVE_RELOAD";
    private static readonly string[] ComposeFiles = { "compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml" };

    internal static bool IsContainer(string marker) => marker == "1";

    internal static bool ShouldLaunch(string root, DockerServeSettings settings, bool? requested, string environment, bool auto)
    {
        if (requested.HasValue) return requested.Value;
        if (string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase)) return false;
        return settings.Enabled ?? (auto && File.Exists(ComposeFile(root, settings)));
    }

    internal static ProcessStartInfo CreateStartInfo(string root, DockerServeSettings settings, string containerName, string environment, bool liveReload = true)
    {
        string file = FindComposeFile(root, settings);
        if (string.IsNullOrWhiteSpace(settings.Service) || settings.Service.StartsWith("-", StringComparison.Ordinal))
            throw new ProjectConfigException("serve.docker.service needs a Compose service name, such as app.");
        if (settings.Mount is string mount && (mount.Length < 2 || mount[0] != '/' || mount.Contains(':')))
            throw new ProjectConfigException("serve.docker.mount needs an absolute container path, such as /app, or null.");

        var start = new ProcessStartInfo("docker") { WorkingDirectory = root, UseShellExecute = false };
        foreach (string arg in new[] { "compose", "--file", file, "run", "--rm", "--service-ports", "--name", containerName, "--env", ContainerMarker + "=1", "--env", MigrateSetting + "=1" })
            start.ArgumentList.Add(arg);
        if (settings.Build) start.ArgumentList.Add("--build");
        if (settings.Mount != null)
        {
            start.ArgumentList.Add("--volume");
            start.ArgumentList.Add($"{root}:{settings.Mount}:ro");
            environment ??= "development";
        }
        if (!string.IsNullOrWhiteSpace(environment))
        {
            start.ArgumentList.Add("--env");
            start.ArgumentList.Add("KSCRIPT_ENV=" + environment);
        }
        if (!liveReload)
        {
            start.ArgumentList.Add("--env");
            start.ArgumentList.Add(ReloadSetting + "=0");
        }
        start.ArgumentList.Add(settings.Service);
        return start;
    }

    private static string ComposeFile(string root, DockerServeSettings settings) =>
        settings.ComposeFile == null
            ? ComposeFiles.Select(name => Path.Combine(root, name)).FirstOrDefault(File.Exists)
            : Path.GetFullPath(settings.ComposeFile, root);

    private static string FindComposeFile(string root, DockerServeSettings settings)
    {
        string file = ComposeFile(root, settings);
        if (file == null || !File.Exists(file))
            throw new ProjectConfigException(file == null
                ? "Docker needs a Compose file in the project. Run kscript make docker to add one."
                : $"Docker Compose file not found: {file}.");
        return file;
    }

    private static int PrepareVolumeMounts(string root, DockerServeSettings settings, TextWriter error, CancellationToken stop)
    {
        // Read the same Compose model that run will use. This resolves long and short volume syntax,
        // variables, includes and extensions without having to parse Compose's YAML ourselves.
        var config = new ProcessStartInfo("docker")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "compose", "--file", FindComposeFile(root, settings), "--profile", "*", "config", "--format", "json", settings.Service },
        };
        using var process = Process.Start(config);
        var output = process.StandardOutput.ReadToEndAsync(stop);
        var errors = process.StandardError.ReadToEndAsync(stop);
        try
        {
            process.WaitForExitAsync(stop).GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                error.Write(errors.GetAwaiter().GetResult());
                return process.ExitCode;
            }
            PrepareVolumeDirectories(root, settings.Mount, settings.Service, output.GetAwaiter().GetResult());
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Compose already exited. */ }
            throw;
        }
    }

    internal static void PrepareVolumeDirectories(string root, string mount, string service, string composeConfig)
    {
        try
        {
            using JsonDocument config = JsonDocument.Parse(composeConfig);
            if (!config.RootElement.TryGetProperty("services", out JsonElement services)
                || !services.TryGetProperty(service, out JsonElement app)
                || !app.TryGetProperty("volumes", out JsonElement volumes))
                return;

            string prefix = mount.TrimEnd('/') + "/";
            var mounts = volumes.EnumerateArray()
                .Select(volume => (Type: volume.GetProperty("type").GetString(), Target: (volume.GetProperty("target").GetString() ?? "").TrimEnd('/')))
                .ToList();
            foreach (var volume in mounts)
            {
                if (volume.Type != "volume" || !volume.Target.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                // Once another Compose mount hides a path, its volume's nested mountpoint is no longer in the source tree.
                if (mounts.Any(parent => parent.Target.StartsWith(prefix, StringComparison.Ordinal)
                    && volume.Target.StartsWith(parent.Target + "/", StringComparison.Ordinal)))
                    continue;
                EnsureVolumeDirectory(root, volume.Target, volume.Target.Substring(prefix.Length));
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ProjectConfigException("Docker Compose returned an invalid volume configuration.");
        }
    }

    private static void EnsureVolumeDirectory(string root, string target, string relative)
    {
        string[] segments = relative.Split('/');
        char[] invalid = Path.GetInvalidFileNameChars();
        if (segments.Any(segment => segment is "" or "." or ".." || segment.IndexOfAny(invalid) >= 0))
            throw new ProjectConfigException($"Docker Compose volume target '{target}' is not a valid subdirectory of the project.");

        string path = root;
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new ProjectConfigException($"Docker Compose volume target '{target}' needs a directory at {path}.");
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Docker cannot create this mountpoint after it makes the parent source bind read-only.
                Directory.CreateDirectory(path);
            }
        }
    }

    internal static int Run(string root, DockerServeSettings settings, TextWriter error, CancellationToken stop, bool liveReload = true, string environment = null)
    {
        if (stop.IsCancellationRequested) return 0;
        string name = "kscript-serve-" + Guid.NewGuid().ToString("N");
        try
        {
            var start = CreateStartInfo(root, settings, name, environment, liveReload);
            if (settings.Mount != null)
            {
                int prepared = PrepareVolumeMounts(root, settings, error, stop);
                if (prepared != 0) return prepared;
            }
            using var process = Process.Start(start);
            try
            {
                process.WaitForExitAsync(stop).GetAwaiter().GetResult();
                return stop.IsCancellationRequested ? 0 : process.ExitCode;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* Compose already exited. */ }
                StopContainer(root, name, error);
                return 0;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or ProjectConfigException)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Usage;
        }
        catch (Win32Exception ex)
        {
            error.WriteLine($"Could not start Docker. Check that Docker and the Compose plugin are installed. {ex.Message}");
            return 1;
        }
    }

    private static void StopContainer(string root, string name, TextWriter error)
    {
        // Stop just this invocation. Compose dependencies and any other app instance can keep running.
        var start = new ProcessStartInfo("docker") { WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "stop", "--time", "3", name } };
        using var process = Process.Start(start);
        if (!process.WaitForExit(8000))
        {
            try { process.Kill(); }
            catch (InvalidOperationException) { /* Docker already exited. */ }
            error.WriteLine($"Docker did not confirm that {name} stopped. Check docker ps.");
        }
    }
}
