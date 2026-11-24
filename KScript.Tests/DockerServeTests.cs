using KScript.Projects;

namespace KScript.Tests;

public sealed class DockerServeTests
{
    [Fact]
    public void DockerConfigChoosesTheServiceAndMountsTheProject()
    {
        using var site = new TempSite(
            ("kscript.json", """{"serve":{"docker":{"compose_file":"compose.dev.yaml","service":"web","build":false,"mount":"/site"}}}"""),
            ("compose.dev.yaml", "services: {}"));
        var config = ProjectConfig.Load(Path.Combine(site.Root, "kscript.json"));
        Assert.Empty(config.Warnings);
        Assert.True(config.Serve.Docker.Enabled);

        var start = DockerServe.CreateStartInfo(config.Directory, config.Serve.Docker, "test-container", null);
        Assert.Equal("docker", start.FileName);
        Assert.Equal(config.Directory, start.WorkingDirectory);
        Assert.Equal(new[]
        {
            "compose", "--file", Path.Combine(site.Root, "compose.dev.yaml"), "run", "--rm", "--service-ports",
            "--name", "test-container", "--env", "KSCRIPT_SERVE_IN_DOCKER=1", "--volume", $"{site.Root}:/site:ro",
            "--env", "KSCRIPT_ENV=development", "web",
        }, start.ArgumentList);
    }

    [Fact]
    public void DockerCanUseBakedFilesAndAnExplicitTestEnvironment()
    {
        using var site = new TempSite(
            ("kscript.json", """{"serve":{"docker":{"enabled":false,"mount":null}}}"""),
            ("docker-compose.yml", "services: {}"));
        var config = ProjectConfig.Load(Path.Combine(site.Root, "kscript.json"));
        Assert.Empty(config.Warnings);
        Assert.False(config.Serve.Docker.Enabled);

        var start = DockerServe.CreateStartInfo(site.Root, config.Serve.Docker, "test-container", "test");
        Assert.Contains("--build", start.ArgumentList);
        Assert.DoesNotContain("--volume", start.ArgumentList);
        Assert.Contains("KSCRIPT_ENV=test", start.ArgumentList);
        Assert.Equal("app", start.ArgumentList.Last());
    }

    [Fact]
    public void ServeUsesConfiguredDockerWithoutStartingALocalServer()
    {
        using var site = new TempSite(("kscript.json", """{"serve":{"docker":true}}"""));
        var errors = new StringWriter();
        var command = new CommandLine { Error = errors };
        Assert.Equal(2, command.Run(new[] { "serve", site.Root }));
        Assert.Contains("kscript make docker", errors.ToString());

        errors.GetStringBuilder().Clear();
        Assert.Equal(2, command.Run(new[] { "serve", site.Root, "--port", "9000" }));
        Assert.Contains("Set Docker ports", errors.ToString());
    }
}
