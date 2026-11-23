using System.Text;
using KScript.Cli;
using KScript.Projects;

namespace KScript.Tests;

public sealed class ProjectLibrariesTests : IDisposable
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private readonly string root = Directory.CreateTempSubdirectory("kscript-dependencies-").FullName;
    private string Manifest => Path.Combine(root, "kscript.json");
    private static LibraryDependency Dependency(string name) => new(name, "https://github.com/acme/libs", Sha);
    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void CreatesManifestAndRepeatedAddPreservesTimestamp()
    {
        var dependency = Dependency("My SDK+");
        ProjectLibraries.Record(root, dependency);
        var config = ProjectConfig.Load(Manifest);
        Assert.Empty(config.Warnings);
        Assert.Equal(dependency, Assert.Single(config.Libraries).Value);
        File.SetLastWriteTimeUtc(Manifest, DateTime.UnixEpoch.AddDays(10));
        var previous = File.GetLastWriteTimeUtc(Manifest);
        ProjectLibraries.Record(root, dependency);
        Assert.Equal(previous, File.GetLastWriteTimeUtc(Manifest));
    }

    [Fact]
    public void EditsNearestCommentedManifestWithoutLoadingStandards()
    {
        string original = $$"""
            {
              // café
              "standard": "./unavailable-standard.json",
              "libraries": {
                // keep this package
                "existing": {"source":"other", "commit":"{{Sha}}"},
              },
              "serve": { "port": 4567, },
            }
            """.ReplaceLineEndings("\r\n");
        File.WriteAllText(Manifest, original, new UTF8Encoding(true));
        string nested = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
        ProjectLibraries.Record(nested, Dependency("hello"));
        byte[] bytes = File.ReadAllBytes(Manifest);
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        string actual = File.ReadAllText(Manifest);
        int insert = original.IndexOf("\"libraries\": {", StringComparison.Ordinal) + "\"libraries\": {".Length;
        Assert.StartsWith(original[..insert] + "\r\n    \"hello\":", actual);
        Assert.EndsWith(original[insert..], actual);
        Assert.False(File.Exists(Path.Combine(nested, ProjectConfig.FileName)));
    }

    [Fact]
    public void ConcurrentRecordsKeepEveryEntry()
    {
        var names = Enumerable.Range(0, 24).Select(i => $"library-{i}").ToArray();
        Parallel.ForEach(names.Concat(names), name => ProjectLibraries.Record(root, Dependency(name)));
        var config = ProjectConfig.Load(Manifest);
        Assert.Equal(names.Length, config.Libraries.Count);
        foreach (string name in names) Assert.Equal(Dependency(name), config.Libraries[name]);
    }

    [Theory]
    [InlineData("HELLO", "https://github.com/acme/libs", Sha)]
    [InlineData("hello", "https://github.com/other/libs", Sha)]
    [InlineData("hello", "https://github.com/acme/libs", "ffffffffffffffffffffffffffffffffffffffff")]
    public void ConflictLeavesTheOriginalManifestIntact(string name, string source, string commit)
    {
        ProjectLibraries.Record(root, Dependency("hello"));
        byte[] original = File.ReadAllBytes(Manifest);
        var error = Assert.Throws<ProjectConfigException>(() => ProjectLibraries.Record(root, new(name, source, commit)));
        Assert.Contains("already records 'hello'", error.Message);
        Assert.Equal(original, File.ReadAllBytes(Manifest));
    }

    [Theory]
    [InlineData("../somewhere")]
    [InlineData("dir\\other")]
    [InlineData(".secret")]
    [InlineData("nUl.txt")]
    [InlineData("library.")]
    [InlineData("library ")]
    public void RejectsUnsafeDependencyDirectory(string name)
    {
        Assert.Throws<ProjectConfigException>(() => ProjectLibraries.Record(root, Dependency(name)));
        Assert.False(File.Exists(Manifest));
    }

    [Theory]
    [InlineData("{\"libraries\":[],\"name\":\"hi\"}", "libraries must be")]
    [InlineData("{\"libraries\":{},\"libraries\":{}}", "Duplicate libraries")]
    [InlineData("{\"libraries\":{\"lib\":{}}}", "source must be")]
    [InlineData("{\"libraries\":{\"lib\":{\"source\":\"github\"}}}", "40-character")]
    [InlineData("{\"libraries\":{\"lib\":{\"source\":\"github\",\"commit\":\"short\"}}}", "40-character")]
    [InlineData("{\"libraries\":{\"lib\":{\"source\":\"github\",\"commit\":\"FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF\"}}}", "lowercase")]
    [InlineData("{\"libraries\":{\"lib\":{\"source\":\"a\",\"source\":\"b\"}}}", "Duplicate libraries.lib.source")]
    public void ReportsMalformedDeclarations(string json, string message)
    {
        var error = Assert.Throws<ProjectConfigException>(() => ProjectConfig.Parse(json, Manifest));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("sdk")]
    [InlineData("Sdk")]
    public void RejectsDuplicateDependencyEvenWithDifferentCasing(string other)
    {
        string entry = $$"""{ "source":"test", "commit":"{{Sha}}" }""";
        string json = "{\"libraries\":{\"sdk\":" + entry + ",\"" + other + "\":" + entry + "}}";
        var error = Assert.Throws<ProjectConfigException>(() => ProjectConfig.Parse(json, Manifest));
        Assert.Contains("unique ignoring case", error.Message);
    }
}
