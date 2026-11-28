using System.Globalization;

using KScript.Projects;
using KScript.Runtime;
using KScript.Storage;

namespace KScript.Tests;

/// <summary>
/// Puts every app's folders under a temporary folder (KSCRIPT_STORAGE_ROOT) for one test, and runs scripts from a
/// temporary project folder. KScript.Tests runs one test at a time, so the variable can't leak into another test.
/// </summary>
public abstract class StorageFixture : IDisposable
{
    protected readonly string Root = Path.Combine(Path.GetTempPath(), "kscript-storage-" + Guid.NewGuid().ToString("N"));
    protected readonly string Project;

    protected StorageFixture()
    {
        Project = Path.Combine(Root, "project");
        Directory.CreateDirectory(Project);
        Environment.SetEnvironmentVariable(AppFolders.RootVariable, Path.Combine(Root, "apps"));
    }

    protected virtual void OnDispose()
    {
    }

    public void Dispose()
    {
        OnDispose();
        Environment.SetEnvironmentVariable(AppFolders.RootVariable, null);
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>Where an app's folder of one kind is while the test runs.</summary>
    protected string AppFolder(string app, string kind) => Path.Combine(Root, "apps", app, kind);

    protected void FileOf(string name, string text)
    {
        string path = Path.Combine(Project, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    /// <summary>Runs a script saved as <paramref name="file"/> in the project folder, with KScript.Storage and KScript.Cache imported.</summary>
    protected string Run(string body, string file = "notes.ks.xml", Action<KScriptHost> setup = null, ConfigOptions config = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output);
        host.Config = config;
        setup?.Invoke(host);
        string path = Path.Combine(Project, file);
        new Interpreter(host).RunText("<kscript><import package=\"KScript.Storage\" /><import package=\"KScript.Cache\" />" + body + "</kscript>", path);
        return output.ToString();
    }

    protected KScriptException Fails(string body, Action<KScriptHost> setup = null) => Assert.Throws<KScriptException>(() => Run(body, setup: setup));
}

public sealed class AppFoldersTests : StorageFixture
{
    private const string Home = "/home/joan";

    private static Func<string, string> Env(params (string Name, string Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value;

    [Theory]
    [InlineData("config", "/home/joan/Library/Application Support/Life Lab")]
    [InlineData("data", "/home/joan/Library/Application Support/Life Lab")]
    [InlineData("state", "/home/joan/Library/Application Support/Life Lab")]
    [InlineData("cache", "/home/joan/Library/Caches/Life Lab")]
    [InlineData("logs", "/home/joan/Library/Logs/Life Lab")]
    public void MacOsUsesApplicationSupportCachesAndLogs(string kind, string expected) =>
        Assert.Equal(expected, AppFolders.Folder("Life Lab", kind, "macos", Env(), Home).Replace('\\', '/'));

    [Theory]
    [InlineData("config", "/r/Life Lab")]
    [InlineData("data", "/l/Life Lab")]
    [InlineData("cache", "/l/Life Lab/Cache")]
    [InlineData("state", "/l/Life Lab/State")]
    [InlineData("logs", "/l/Life Lab/Logs")]
    public void WindowsUsesRoamingForSettingsAndLocalForTheRest(string kind, string expected) =>
        Assert.Equal(expected, AppFolders.Folder("Life Lab", kind, "windows", Env(("APPDATA", "/r"), ("LOCALAPPDATA", "/l")), Home).Replace('\\', '/'));

    [Fact]
    public void WindowsFallsBackToTheHomeFolder()
    {
        Assert.Equal("/home/joan/AppData/Roaming/Notes", AppFolders.Folder("Notes", "config", "windows", Env(), Home).Replace('\\', '/'));
        Assert.Equal("/home/joan/AppData/Local/Notes", AppFolders.Folder("Notes", "data", "windows", Env(("LOCALAPPDATA", "relative")), Home).Replace('\\', '/'));
    }

    [Theory]
    [InlineData("config", "/home/joan/.config/life-lab")]
    [InlineData("data", "/home/joan/.local/share/life-lab")]
    [InlineData("cache", "/home/joan/.cache/life-lab")]
    [InlineData("state", "/home/joan/.local/state/life-lab")]
    [InlineData("logs", "/home/joan/.local/state/life-lab/logs")]
    public void LinuxUsesTheXdgFolders(string kind, string expected) =>
        Assert.Equal(expected, AppFolders.Folder("Life  Lab", kind, "linux", Env(), Home).Replace('\\', '/'));

    [Fact]
    public void RelativeProjectDataCanLiveOutsideTheServedFolder()
    {
        const string variable = "KSCRIPT_PROJECT_DATA_ROOT";
        string old = Environment.GetEnvironmentVariable(variable);
        string privateFolder = Path.Combine(Root, "private");
        try
        {
            Environment.SetEnvironmentVariable(variable, privateFolder);
            Assert.Equal(Path.Combine(privateFolder, "data", "app.sqlite"), AppFolders.ProjectDataPath(Project, "data/app.sqlite"));
            Assert.Equal(Project, AppFolders.ProjectDataPath(Project, Project));
            Assert.Throws<KScriptException>(() => AppFolders.ProjectDataPath(Project, "../escape"));
            Environment.SetEnvironmentVariable(variable, null);
            Assert.Equal(Path.Combine(Project, "data"), AppFolders.ProjectDataPath(Project, "data"));
        }
        finally { Environment.SetEnvironmentVariable(variable, old); }
    }

    [Fact]
    public void XdgVariablesWinWhenTheyAreAbsolute()
    {
        var env = Env(("XDG_CONFIG_HOME", "/x/config"), ("XDG_DATA_HOME", "/x/data"), ("XDG_CACHE_HOME", "/x/cache"), ("XDG_STATE_HOME", "relative/state"));
        Assert.Equal("/x/config/notes", AppFolders.Folder("Notes", "config", "linux", env, Home).Replace('\\', '/'));
        Assert.Equal("/x/data/notes", AppFolders.Folder("Notes", "data", "linux", env, Home).Replace('\\', '/'));
        Assert.Equal("/x/cache/notes", AppFolders.Folder("Notes", "cache", "freebsd", env, Home).Replace('\\', '/'));
        Assert.Equal("/home/joan/.local/state/notes", AppFolders.Folder("Notes", "state", "linux", env, Home).Replace('\\', '/'));
    }

    [Fact]
    public void TheRootVariableMovesEveryFolder()
    {
        Assert.Equal("/portable/Notes/cache", AppFolders.Folder("Notes", "cache", "macos", Env((AppFolders.RootVariable, "/portable")), Home).Replace('\\', '/'));
        Assert.Equal(AppFolder("Notes", "logs"), AppFolders.Folder("Notes", "logs"));
        Assert.Contains(AppFolders.CurrentPlatform, new[] { "macos", "windows", "linux" });
    }

    [Fact]
    public void NamesAndKindsAreChecked()
    {
        Assert.Equal("a-b-c", AppFolders.CleanName(" a/b:c "));
        Assert.Equal("Notes", AppFolders.CleanName("..Notes."));
        Assert.Contains("can't be used as an app name", Assert.Throws<KScriptException>(() => AppFolders.CleanName(" .. ")).Message);
        Assert.Contains("can't be used as an app name", Assert.Throws<KScriptException>(() => AppFolders.CleanName(null)).Message);
        Assert.Equal("data", AppFolders.CheckKind(null));
        Assert.Equal("cache", AppFolders.CheckKind(" Cache "));
        Assert.Contains("Use one of: config, data, cache, state, logs", Assert.Throws<KScriptException>(() => AppFolders.CheckKind("temp")).Message);
    }

    [Fact]
    public void TheAppNameComesFromKscriptJsonOrTheScript()
    {
        Assert.Equal("kscript", AppFolders.AppNameFor(null, null));
        Assert.Equal("notes", AppFolders.AppNameFor(Project, Path.Combine(Project, "notes.ks.xml")));
        Assert.Equal("Makefile", AppFolders.AppNameFor(Project, Path.Combine(Project, "Makefile")));

        FileOf("kscript.json", "{ \"name\": \"shop\" }");
        Assert.Equal("shop", AppFolders.AppNameFor(Project, null));
        FileOf("kscript.json", "{ \"name\": \"shop\", \"app\": { \"name\": \" Shop Till \" } }");
        Assert.Equal("Shop Till", AppFolders.AppNameFor(Path.Combine(Project, "sub"), null));
        FileOf("kscript.json", "{ \"name\": \"  \" }");
        Assert.Equal("kscript", AppFolders.AppNameFor(Project, null));
        FileOf("kscript.json", "{ broken");
        Assert.Equal("x", AppFolders.AppNameFor(Project, "x.kscript"));
    }

    [Fact]
    public void ProjectsNameTheirAppAndCanReadTheUsersConfig()
    {
        ProjectConfig named = ProjectConfig.Parse("{ \"name\": \"shop\", \"app\": { \"user_config\": true } }", Path.Combine(Project, "kscript.json"));
        Assert.Empty(named.Warnings);
        Assert.True(named.App.UserConfig);
        Assert.Equal("shop", named.AppName);
        Assert.Equal(AppFolder("shop", "config"), named.ConfigOptions().UserFolder);

        ProjectConfig app = ProjectConfig.Parse("{ \"name\": \"shop\", \"app\": { \"name\": \"Shop\" } }", Path.Combine(Project, "kscript.json"));
        Assert.Equal("Shop", app.AppName);
        Assert.Null(app.ConfigOptions().UserFolder);
        Assert.Equal("project", ProjectConfig.Parse("{}", Path.Combine(Project, "kscript.json")).AppName);
        Assert.Equal("kscript", new ProjectConfig().AppName);
        Assert.Contains(ProjectConfig.Parse("{ \"app\": { \"user_config\": 1 } }", Path.Combine(Project, "kscript.json")).Warnings, w => w.Contains("app.user_config"));
    }
}

public sealed class StorageTests : StorageFixture
{
    [Fact]
    public void ReadsAndWritesTextAndJson()
    {
        string output = Run("""
            <let name="config" value="@storage(config)" />
            <echo>{ $config->read('missing.txt') ?? 'none' } { $config->read_json('settings.json', default: { theme: 'light' }).theme }</echo>
            <do>$config->write_json('settings.json', { theme: 'dark' }); $config->write('notes/today.txt', 'milk')</do>
            <do>$config->append('log.txt', 'a'); $config->append('log.txt', 'b')</do>
            <echo>{ $config->read_json('settings.json').theme } { $config->read('notes/today.txt') } { $config->read('log.txt') }</echo>
            <echo>{ $config.kind } { $config.app } { $config.path == $config.path } { $config->to_string() == $config.path }</echo>
            """);
        Assert.Equal("none light\ndark milk ab\nconfig notes true true\n", output);
        Assert.Equal("{\n  \"theme\": \"dark\"\n}", File.ReadAllText(Path.Combine(AppFolder("notes", "config"), "settings.json")).Replace("\r\n", "\n"));
        Assert.Empty(Directory.GetFiles(AppFolder("notes", "config"), "*.tmp-*"));
    }

    [Fact]
    public void ReadJsonMapsExistingFilesAndLeavesMissingFileDefaultsAlone()
    {
        const string user = "<type name=\"User\" fields=\"id: int\" />";
        string output = Run(user + """
            <let name="data" value="@storage(data)" />
            <do>$data->write_json('users.json', [{ id: 7 }])</do>
            <let name="first" value="$data->read_json('users.json', as: User)->first()" />
            <echo>{ $first->type() } { $first.id }</echo>
            <echo>{ $data->read_json('missing.json', as: User) == null } { $data->read_json('missing.json', default: 'unknown', as: User) }</echo>
            """);
        Assert.Equal("User 7\ntrue unknown\n", output);

        KScriptException error = Fails(user + """
            <do>@storage(data)->write_json('user.json', { id: 'wrong' })</do>
            <do>@storage(data)->read_json('user.json', 'fallback', User)</do>
            """);
        Assert.Equal("Shape", error.Type);
        Assert.Contains("$.id: needs an int", error.Message);
    }

    [Fact]
    public void ListsDeletesAndClears()
    {
        string output = Run("""
            <let name="data" value="@storage(data)" />
            <echo>{ $data->exists() } { $data->list() } { $data->clear() }</echo>
            <do>$data->write('b.txt', '1'); $data->write('a.json', '2'); $data->write('notes/c.txt', '3'); $data->write('notes/deep/d.txt', '4')</do>
            <echo>{ $data->exists() } { $data->exists('notes') } { $data->exists('nope.txt') }</echo>
            <echo>{ $data->list() } | { $data->list('*.txt', recursive) } | { $data->list(pattern: null) }</echo>
            <echo>{ $data->list(/^notes\/.*\.txt$/, recursive) } | { $data->list(/\.json$/) }</echo>
            <echo>{ $data->folder('notes')->list() } { $data->folder('notes').path->ends_with('notes') } { $data->path_of('notes/c.txt')->ends_with('c.txt') }</echo>
            <echo>{ $data->delete('a.json') } { $data->delete('a.json') } { $data->delete('notes/deep') }</echo>
            <echo>{ $data->clear() } { $data->list(recursive) }</echo>
            """);
        Assert.Equal("false  0\ntrue true false\na.json, b.txt | b.txt, notes/c.txt, notes/deep/d.txt | a.json, b.txt\nnotes/c.txt, notes/deep/d.txt | a.json\nc.txt true true\ntrue false true\n2\n", output);
    }

    [Fact]
    public void AListPatternMustStayInsideTheFolder()
    {
        Directory.CreateDirectory(AppFolder("notes", "config"));
        File.WriteAllText(Path.Combine(AppFolder("notes", "config"), "secret.txt"), "x");
        Run("<do>@storage(data)->write('notes/c.txt', '3')</do>");
        foreach (string pattern in new[] { "../*", "../config/*", "notes/../../config/*", "..\\config\\*", "/etc/host*" })
        {
            KScriptException error = Fails($"<echo>{{ @storage(data)->list('{pattern}', recursive) }}</echo>");
            Assert.Equal("Permission", error.Type);
            Assert.Contains("isn't inside the app's data folder", error.Message);
        }
        Assert.Equal("notes/c.txt|\n", Run("<echo>{ @storage(data)->list('notes/*.txt') }|{ @storage(data)->list('nowhere/*.txt') }</echo>"));
    }

    [Fact]
    public void AppendStartsAFileWithoutAByteOrderMarkAndAFailedWriteLeavesNothingBehind()
    {
        Run("<do>@storage(data)->append('log.txt', 'héllo'); @storage(data)->write('notes/c.txt', '3')</do>");
        Assert.Equal(new byte[] { 0x68, 0xC3, 0xA9, 0x6C, 0x6C, 0x6F }, File.ReadAllBytes(Path.Combine(AppFolder("notes", "data"), "log.txt")));
        // notes is a folder, so the file can't be put in its place.
        Assert.ThrowsAny<Exception>(() => Run("<do>@storage(data)->write('notes', 'x')</do>"));
        Assert.Empty(Directory.GetFiles(AppFolder("notes", "data"), "*.tmp-*"));
    }

    [Fact]
    public void NamesMustStayInsideTheFolder()
    {
        Assert.Contains("isn't inside the app's data folder", Fails("<echo>{ @storage()->read('../other/x.txt') }</echo>").Message);
        Assert.Contains("isn't inside the app's data folder", Fails("<echo>{ @storage()->read('/etc/passwd') }</echo>").Message);
        Assert.Contains("The file name is empty", Fails("<echo>{ @storage()->read(' ') }</echo>").Message);
        Assert.Contains("isn't a valid kind", Fails("<echo>{ @storage('temp') }</echo>").Message);
    }

    [Fact]
    public void ReadOnlyHostsCanReadButNotWrite()
    {
        Run("<do>@storage(state)->write('recent.json', '[1]')</do>");
        void ReadOnly(KScriptHost h) => h.CanWriteFiles = false;
        Assert.Equal("[1]\n", Run("<echo>{ @storage(state)->read('recent.json') }</echo>", setup: ReadOnly));
        foreach (string call in new[] { "write('a', 'b')", "append('a', 'b')", "write_json('a', 1)", "delete('a')", "clear()" })
            Assert.Contains("can't change files here", Fails($"<do>@storage(state)->{call}</do>", ReadOnly).Message);
    }

    [Fact]
    public void TheAppComesFromTheArgumentKscriptJsonOrTheScript()
    {
        Assert.Equal("notes\n", Run("<echo>{ @app_name() }</echo>"));
        Assert.Equal("Other\n", Run("<echo>{ @storage(cache, app: 'Other').app }</echo>"));
        FileOf("kscript.json", "{ \"app\": { \"name\": \"Notes App\" } }");
        Assert.Equal("Notes App\n", Run("<echo>{ @app_name() }</echo>"));
        Assert.Equal(AppFolder("Notes App", "logs") + "\n", Run("<echo>{ @app_folders().logs }</echo>"));
        Assert.Equal(AppFolder("X", "cache") + "\n", Run("<echo>{ @app_folders(app: 'X').cache }</echo>"));
        Assert.Equal(AppFolder("Notes App", "data") + "\n", Run("<echo>{ @storage() }</echo>"));
    }

    [Fact]
    public void FolderValuesCompareByPathAndHaveNoOtherMembers()
    {
        var a = new StorageFolder("A", "data", "/tmp/a");
        Assert.Equal(new StorageFolder("B", "cache", "/tmp/a"), a);
        Assert.NotEqual((object)a, "/tmp/a");
        Assert.Equal(a.Path.GetHashCode(), a.GetHashCode());
        Assert.Equal(a.Path, a.ToString());
        Assert.Equal(a.Path, a.ToText());
        Assert.Equal("storage", a.TypeName);
        Assert.False(a.TryGetMember("size", out _));
        Assert.False(a.TryCallMethod(null, "read", out _));
        var call = new CallContext(new Interpreter(), "nope", new List<object>());
        Assert.False(a.Invoke(call, "nope", out object result));
        Assert.Null(result);
        Assert.NotEmpty(new StoragePackage().Description);
    }

    [Fact]
    public void ConfigUserReadsThePersonsOwnSettings()
    {
        FileOf("kscript.json", "{ \"name\": \"notes\", \"config\": { \"greeting\": \"Hello\", \"size\": 1 } }");
        Directory.CreateDirectory(AppFolder("notes", "config"));
        File.WriteAllText(Path.Combine(AppFolder("notes", "config"), "config.json"), "{ \"greeting\": \"Hi\" }");

        Assert.Equal("Hello 1\n", Run("<echo>{ $config.greeting } { $config.size }</echo>"));
        Assert.Equal("Hi 1 file\n", Run("<config user=\"yes\" /><echo>{ $config.greeting } { $config.size } { $config->sources()->last().kind }</echo>"));
        Assert.Equal("Hello\n", Run("<config user=\"no\" /><echo>{ $config.greeting }</echo>", config: new ConfigOptions { UserFolder = AppFolder("notes", "config") }));
        Assert.Equal("Hi\n", Run("<echo>{ $config.greeting }</echo>", config: new ConfigOptions { UserFolder = AppFolder("notes", "config") }));
        Assert.Equal("Hello\n", Run("<echo>{ $config.greeting }</echo>", config: new ConfigOptions { UserFolder = Path.Combine(Root, "nothing-here") }));

        // A script can save a setting for the person, and $config->reload() sees it.
        Assert.Equal("Hi Bye\n", Run("<config user=\"yes\" /><echo>{ $config.greeting } { @storage(config)->write_json('config.json', { greeting: 'Bye' }) ?? $config->reload().greeting }</echo>"));
    }

    [Fact]
    public void TheEditorKnowsTheNewPackages()
    {
        var catalog = KScript.Language.Catalog.LanguageCatalog.Default;
        Assert.Equal("KScript.Storage", catalog.GetCommand("storage").Package);
        Assert.Equal("storage", catalog.GetCommand("storage").ReturnType);
        Assert.Contains(catalog.Methods, m => m.ReceiverType == "storage" && m.Name == "read_json" && m.Example != null);
        Assert.Contains(catalog.Methods, m => m.ReceiverType == "cache" && m.Name == "remember" && m.Example != null);
        Assert.Equal("KScript.Cache", catalog.GetCommand("cache").Package);
        Assert.Equal("KScript.Http", catalog.GetCommand("etag").Package);
    }

    [Fact]
    public void TheSampleNotesAppWorks()
    {
        string sample = ScriptRunner.RepoFile("samples", "storage", "notes.kscript");
        string Notes(params string[] args)
        {
            var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
            var host = KScriptHost.ForText(output);
            host.Arguments = args;
            host.Config = ProjectConfig.Load(ScriptRunner.RepoFile("samples", "storage", "kscript.json")).ConfigOptions();
            new Interpreter(host).RunFile(sample);
            return output.ToString();
        }
        Assert.Equal("Added: Buy milk\n", Notes("add", "Buy", "milk"));
        Assert.Equal($"Your notes (1 views, in {AppFolder("Notes", "data")})\n- Buy milk\n", Notes());
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(AppFolder("Notes", "config")).FullName, "config.json"), "{ \"greeting\": \"Shopping\" }");
        Assert.StartsWith("Shopping (2 views", Notes());
        Assert.Equal("All notes removed.\n", Notes("clear"));
        Assert.False(File.Exists(Path.Combine(AppFolder("Notes", "data"), "notes.json")));
    }
}
