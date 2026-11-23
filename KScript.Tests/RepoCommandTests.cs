using KScript.Cli;

namespace KScript.Tests;

public sealed class RepoCommandTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("kscript-repos-").FullName;
    public void Dispose() => Directory.Delete(root, recursive: true);

    private int Add(string repository, string cwd = null) =>
        RepoCommand.Run(new[] { "add", repository }, TextWriter.Null, TextWriter.Null, cwd ?? root);

    [Theory]
    [InlineData("kylepantall/kscript")]
    [InlineData("waggr-gb/isolink-connect")]
    [InlineData("waggr-gb/new-library")]
    [InlineData("orimovo/another-repository")]
    public void OfficialRepositoriesAreAvailableWithoutWritingARegistry(string repository)
    {
        Assert.True(LibraryRepositories.Contains(root, repository));
        Assert.Equal(0, Add($"https://github.com/{repository.ToUpperInvariant()}.git"));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Theory]
    [InlineData("waggr-gb-labs/isolink-connect")]
    [InlineData("not-orimovo/sdk")]
    [InlineData("kylepantall/another-repo")]
    public void OtherOwnersAndRepositoriesNeedRegistration(string repository)
    {
        Assert.False(LibraryRepositories.Contains(root, repository));
        Assert.Equal(0, Add(repository));
        Assert.True(LibraryRepositories.Contains(root, repository));
    }

    [Fact]
    public void IndependentRegistrationsSurviveConcurrentAndRepeatedAdds()
    {
        Parallel.For(0, 12, i => Assert.Equal(0, Add($"org/library-{i % 3}")));
        for (int i = 0; i < 3; i++) Assert.True(LibraryRepositories.Contains(root, $"org/library-{i}"));
        string otherProject = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        Assert.False(LibraryRepositories.Contains(otherProject, "org/library-0"));
    }

    [Fact]
    public void DiscoveryUsesOnlyCanonicalMarkersInThisProject()
    {
        Assert.Equal(0, Add("third/catalog"));
        Assert.Equal(0, Add("another/tools"));
        string folder = Path.Combine(root, ".kscript", "repos");
        File.WriteAllText(Path.Combine(folder, "README.md"), "notes");
        File.WriteAllText(Path.Combine(folder, "BAD@NAME"), "invalid marker");
        File.WriteAllText(Path.Combine(folder, "owner@repo@subdir"), "invalid marker");
        if (!OperatingSystem.IsWindows())
            File.CreateSymbolicLink(Path.Combine(folder, "foreign@catalog"), Path.Combine(folder, "third@catalog"));
        Assert.Equal(new[] { "another/tools", "third/catalog" }, LibraryRepositories.Registered(root));
    }

    [Theory]
    [InlineData("org/repo@library")]
    [InlineData("https://github.com/org/repo/tree/main")]
    [InlineData("https://example.org/org/repo")]
    public void InvalidRegistrationsLeaveNoState(string repository)
    {
        Assert.Equal(ExitCodes.Usage, Add(repository));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void RegistrationDoesNotFollowASymbolicLinkOutsideTheProject()
    {
        if (OperatingSystem.IsWindows()) return;
        string outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        string project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(project, ".kscript"), outside);
        Assert.Equal(ExitCodes.Failed, Add("org/library", project));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }
}
