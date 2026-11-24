using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using KScript.Output;
using KScript.Projects;

namespace KScript;

internal static class DockerServe
{
    internal const string ContainerMarker = "KSCRIPT_SERVE_IN_DOCKER";
    private static readonly string[] ComposeFiles = { "compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml" };

    internal static ProcessStartInfo CreateStartInfo(string root, DockerServeSettings settings, string containerName, string environment)
    {
        string file = settings.ComposeFile == null
            ? ComposeFiles.Select(name => Path.Combine(root, name)).FirstOrDefault(File.Exists)
            : Path.GetFullPath(settings.ComposeFile, root);
        if (file == null || !File.Exists(file))
            throw new ProjectConfigException(file == null
                ? "Docker needs a Compose file in the project. Run kscript make docker to add one."
                : $"Docker Compose file not found: {file}.");
        if (string.IsNullOrWhiteSpace(settings.Service) || settings.Service.StartsWith("-", StringComparison.Ordinal))
            throw new ProjectConfigException("serve.docker.service needs a Compose service name, such as app.");
        if (settings.Mount is string mount && (mount.Length < 2 || mount[0] != '/' || mount.Contains(':')))
            throw new ProjectConfigException("serve.docker.mount needs an absolute container path, such as /app, or null.");

        var start = new ProcessStartInfo("docker") { WorkingDirectory = root, UseShellExecute = false };
        foreach (string arg in new[] { "compose", "--file", file, "run", "--rm", "--service-ports", "--name", containerName, "--env", ContainerMarker + "=1" })
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
        start.ArgumentList.Add(settings.Service);
        return start;
    }

    internal static int Run(string root, DockerServeSettings settings, TextWriter error, CancellationToken stop)
    {
        if (stop.IsCancellationRequested) return 0;
        string name = "kscript-serve-" + Guid.NewGuid().ToString("N");
        try
        {
            var start = CreateStartInfo(root, settings, name, Environment.GetEnvironmentVariable("KSCRIPT_ENV"));
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
        catch (Exception ex) when (ex is IOException or ArgumentException or ProjectConfigException)
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
