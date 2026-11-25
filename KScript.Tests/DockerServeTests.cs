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
            "--name", "test-container", "--env", "KSCRIPT_SERVE_IN_DOCKER=1", "--env", "KSCRIPT_SERVE_MIGRATE=1", "--volume", $"{site.Root}:/site:ro",
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

    [Theory]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"auto\"", true)]
    [InlineData("{\"enabled\":\"auto\"}", true)]
    public void LocalDockerDefaultsCanBeOverriddenInProjectConfig(string value, bool expected)
    {
        string config = value == "" ? "{}" : "{\"serve\":{\"docker\":" + value + "}}";
        using var site = new TempSite(("kscript.json", config), ("compose.yaml", "services: {}"));
        var project = ProjectConfig.Load(Path.Combine(site.Root, "kscript.json"));
        Assert.Empty(project.Warnings);
        var settings = project.Serve.Docker;
        Assert.Equal(expected, DockerServe.ShouldLaunch(site.Root, settings, null, "development", true));
        Assert.False(DockerServe.ShouldLaunch(site.Root, settings, null, "production", true));
        Assert.False(DockerServe.ShouldLaunch(site.Root, settings, false, "development", true));
        Assert.True(DockerServe.ShouldLaunch(site.Root, settings, true, "production", false));
        Assert.False(DockerServe.ShouldLaunch(site.Root, new DockerServeSettings(), null, "development", false));
        System.IO.File.Delete(Path.Combine(site.Root, "compose.yaml"));
        Assert.False(DockerServe.ShouldLaunch(site.Root, new DockerServeSettings(), null, "development", true));
    }

    [Fact]
    public void NestedVolumesHaveMountpointsBeforeTheSourceIsMountedReadOnly()
    {
        using var site = new TempSite(("app/state/keep.txt", "user data"));
        const string composeConfig = """
            {
              "services": {
                "app": {
                  "volumes": [
                    { "type": "volume", "target": "/app/data" },
                    { "type": "volume", "target": "/app/data/deeper" },
                    { "type": "volume", "target": "/app/app/state" },
                    { "type": "volume", "target": "/app/other/cache" },
                    { "type": "volume", "target": "/application/not-nested" },
                    { "type": "volume", "target": "/var/lib/app-data" }
                  ]
                },
                "db": { "volumes": [ { "type": "volume", "target": "/app/db-data" } ] }
              }
            }
            """;

        DockerServe.PrepareVolumeDirectories(site.Root, "/app/", "app", composeConfig);
        DockerServe.PrepareVolumeDirectories(site.Root, "/app", "app", composeConfig);
        Assert.True(Directory.Exists(Path.Combine(site.Root, "data")));
        Assert.True(Directory.Exists(Path.Combine(site.Root, "other", "cache")));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(site.Root, "app", "state", "keep.txt")));
        Assert.False(Directory.Exists(Path.Combine(site.Root, "data", "deeper")));
        Assert.False(Directory.Exists(Path.Combine(site.Root, "db-data")));
        Assert.False(Directory.Exists(Path.Combine(site.Root, "not-nested")));
        Assert.False(Directory.Exists(Path.Combine(site.Root, "app-data")));
    }

    [Fact]
    public void VolumeMountpointReportsAFileCollisionWithoutReplacingIt()
    {
        using var site = new TempSite(("data", "user data"));
        const string composeConfig = """{"services":{"app":{"volumes":[{"type":"volume","target":"/app/data"}]}}}""";
        var error = Assert.Throws<ProjectConfigException>(() =>
            DockerServe.PrepareVolumeDirectories(site.Root, "/app", "app", composeConfig));
        Assert.Contains("/app/data", error.Message);
        Assert.Equal("user data", File.ReadAllText(Path.Combine(site.Root, "data")));
    }

    [Fact]
    public void VolumeMountpointCannotUseAParentDirectory()
    {
        using var site = new TempSite();
        const string composeConfig = """{"services":{"app":{"volumes":[{"type":"volume","target":"/app/data/../other"}]}}}""";
        Assert.Throws<ProjectConfigException>(() =>
            DockerServe.PrepareVolumeDirectories(site.Root, "/app", "app", composeConfig));
        Assert.Empty(Directory.EnumerateFileSystemEntries(site.Root));
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
