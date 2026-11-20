using KScript.Caching;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class CacheTests : StorageFixture
{
    private DateTime now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    public CacheTests()
    {
        CacheStore.Clock = () => now;
        // Each test gets its own app name (the script's), so memory stores never carry over between tests.
        ScriptFile = "app" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ks.xml";
    }

    private string ScriptFile { get; }

    private string Run(string body) => base.Run(body, ScriptFile);

    private void Later(double seconds) => now = now.AddSeconds(seconds);

    [Fact]
    public void KeepsValuesInMemoryUntilTheyRunOut()
    {
        Assert.Equal("none 1 true\n", Run("<echo>{ $cache->get('a', default: 'none') } { $cache->put('a', 1, ttl: '1m') } { $cache->has('a') }</echo>"));
        Assert.Equal("1 60\n", Run("<echo>{ $cache->get('a') } { $cache->ttl('a') }</echo>"));
        Later(61);
        Assert.Equal("false\n", Run("<echo>{ $cache->has('a') } { $cache->get('a') } { $cache->ttl('a') }</echo>"));
        Assert.Equal("2\n", Run("<echo>{ $cache->put('forever', 2) } { $cache->ttl('forever') }</echo>"));
    }

    [Fact]
    public void RememberComputesOnlyWhenNeeded()
    {
        const string script = "<let name=\"n\" value=\"0\" /><let name=\"v\" value=\"$cache->remember('k', fn() => 'computed', ttl: 30)\" /><echo>{ $v }</echo>";
        Assert.Equal("computed\n", Run(script));
        Assert.Equal("kept\n", Run("<do>$cache->put('k', 'kept')</do><echo>{ $cache->remember('k', fn() => 'computed') }</echo>"));
        Assert.Contains("needs a function", Assert.Throws<KScriptException>(() => Run("<echo>{ $cache->remember('other', 5) }</echo>")).Message);
    }

    [Fact]
    public void ForgetsIncrementsListsAndFlushes()
    {
        Assert.Equal("1 3 3\n", Run("<echo>{ $cache->increment('n') } { $cache->increment('n', by: 2) } { $cache->get('n') }</echo>"));
        Assert.Equal("1 60 2 60\n", Run("<echo>{ $cache->increment('t', ttl: '1m') } { $cache->ttl('t') } { $cache->increment('t') } { $cache->ttl('t') }</echo>"));
        Assert.Equal("n, t true false\n", Run("<echo>{ $cache->keys() } { $cache->forget('t') } { $cache->forget('t') }</echo>"));
        Assert.Contains("The value of 'word'", Assert.Throws<KScriptException>(() => Run("<do>$cache->put('word', 'abc')</do><echo>{ $cache->increment('word') }</echo>")).Message);
        Assert.Equal("2 0\n", Run("<echo>{ $cache->flush() } { $cache->keys()->count() }</echo>"));
    }

    [Fact]
    public void ValuesAreCopiesSoChangingOneDoesNotChangeTheCache()
    {
        Assert.Equal("1 1\n", Run("""
            <let name="list" value="[1]" />
            <do>$cache->put('list', $list); $list->push(2)</do>
            <let name="got" value="$cache->get('list')" />
            <do>$got->push(3)</do>
            <echo>{ $cache->get('list')->count() } { $cache->get('list')[0] }</echo>
            """));
        Assert.Equal("Joan\n", Run("<do>$cache->put('map', { name: 'Joan', tags: ['a'] })</do><echo>{ $cache->get('map').name }</echo>"));
    }

    [Fact]
    public void KeysMustNotBeEmpty() =>
        Assert.Contains("can't be empty", Assert.Throws<KScriptException>(() => Run("<echo>{ $cache->get('') }</echo>")).Message);

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1.5", 1.5)]
    [InlineData("'500ms'", 0.5)]
    [InlineData("'30s'", 30)]
    [InlineData("'10m'", 600)]
    [InlineData("'2h'", 7200)]
    [InlineData("'1d'", 86400)]
    [InlineData("'1W'", 604800)]
    [InlineData("' 45 '", 45)]
    public void TimeLimitsTakeSecondsOrUnits(string ttl, double seconds)
    {
        Run($"<do>$cache->put('x', 1, ttl: {ttl})</do>");
        Later(seconds - 0.25);
        Assert.Equal("true\n", Run("<echo>{ $cache->has('x') }</echo>"));
        Later(0.5);
        Assert.Equal("false\n", Run("<echo>{ $cache->has('x') }</echo>"));
    }

    [Theory]
    [InlineData("0", "more than 0 seconds")]
    [InlineData("-5", "more than 0 seconds")]
    [InlineData("'soon'", "isn't a time limit")]
    [InlineData("'0s'", "more than 0 seconds")]
    public void BadTimeLimitsAreExplained(string ttl, string message) =>
        Assert.Contains(message, Assert.Throws<KScriptException>(() => Run($"<do>$cache->put('x', 1, ttl: {ttl})</do>")).Message);

    [Fact]
    public void SecondsHandlesEveryKindOfValue()
    {
        Assert.Null(CachePackage.Seconds(null));
        Assert.Equal(5, CachePackage.Seconds(5.0));
        Assert.Throws<KScriptException>(() => CachePackage.Seconds(double.NaN));
        Assert.Throws<KScriptException>(() => CachePackage.Seconds(double.PositiveInfinity));
        Assert.Equal(60, CachePackage.Seconds("60"));
    }

    [Fact]
    public void FileStoresLastBetweenRunsInTheCacheFolder()
    {
        string app = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(ScriptFile));
        Assert.Equal("file default\n", Run("<let name=\"c\" value=\"@cache(file)\" /><echo>{ $c.kind } { $c.name }</echo>"));
        Assert.Equal("[1,2] true\n", Run("<let name=\"c\" value=\"@cache(file, name: 'weather')\" /><echo>{ $c->put('today', [1, 2], ttl: '1h')->to_json() } { $c->has('today') }</echo>"));
        string folder = Path.Combine(AppFolder(app, "cache"), "kscript-cache", "weather");
        Assert.Single(Directory.GetFiles(folder, "*.json"));

        Assert.Equal("1, 2 3600 today\n", Run("<let name=\"c\" value=\"@cache(file, name: 'weather')\" /><echo>{ $c->get('today') } { $c->ttl('today') } { $c->keys() }</echo>"));
        Assert.Equal("true false false\n", Run("<let name=\"c\" value=\"@cache(file, name: 'weather')\" /><echo>{ $c->forget('today') } { $c->forget('today') } { $c->has('today') }</echo>"));

        Run("<let name=\"c\" value=\"@cache(file, name: 'weather')\" /><do>$c->put('a', 1, ttl: 10); $c->put('b', 2)</do>");
        Later(11);
        Assert.Equal("b 1\n", Run("<let name=\"c\" value=\"@cache(file, name: 'weather')\" /><echo>{ $c->keys() } { $c->get('a') ?? 1 }</echo>"));
        Assert.Single(Directory.GetFiles(folder, "*.json"));
        Assert.Equal("1 0\n", Run("<let name=\"c\" value=\"@cache(file, name: 'weather')\" /><echo>{ $c->flush() } { $c->flush() } { $c->keys() }</echo>"));
    }

    [Fact]
    public void BrokenCacheFilesCountAsMissing()
    {
        var store = new FileCacheStore("t", Path.Combine(Root, "files"));
        Assert.Empty(store.Keys());
        Assert.Equal(0, store.Flush());
        Assert.False(store.Forget("a"));
        store.Put("a", 1.0, null);
        string file = Directory.GetFiles(store.Folder).Single();

        File.WriteAllText(file, "{ not json");
        Assert.False(store.TryGet("a", out _, out _));
        File.WriteAllText(file, "[1]");
        Assert.False(store.TryGet("a", out _, out _));
        File.WriteAllText(file, "{ \"key\": 3 }");
        Assert.False(store.TryGet("a", out _, out _));
        // Another key's file at the same place (a hash collision) isn't this key's value.
        File.WriteAllText(file, "{ \"key\": \"b\", \"value\": 2 }");
        Assert.False(store.TryGet("a", out _, out _));
        Assert.False(store.Forget("a"));
        Assert.Equal(new[] { "b" }, store.Keys());
    }

    [Fact]
    public void MemoryStoresDropOldEntriesWhenFull()
    {
        MemoryCacheStore store = MemoryCacheStore.Named("full-" + Guid.NewGuid().ToString("N"));
        store.Put("old", 1.0, now.AddSeconds(1));
        for (int i = 0; i < MemoryCacheStore.MaxEntries - 2; i++) store.Put("k" + i, 1.0, null);
        store.Put("soon", 1.0, now.AddSeconds(100));
        Later(2);
        store.Put("over1", 1.0, null);
        Assert.False(store.TryGet("old", out _, out _));
        store.Put("over2", 1.0, null);
        Assert.False(store.TryGet("soon", out _, out _));
        Assert.True(store.TryGet("over2", out _, out _));
        Assert.Equal(MemoryCacheStore.MaxEntries, store.Keys().Count());
    }

    [Fact]
    public void StoresDescribeThemselves()
    {
        CacheStore store = MemoryCacheStore.Named("describe");
        Assert.Same(store, MemoryCacheStore.Named("describe"));
        Assert.Equal("memory cache 'describe'", store.ToString());
        Assert.Equal("cache", store.TypeName);
        Assert.True(store.TryGetMember("kind", out object kind));
        Assert.Equal("memory", kind);
        Assert.False(store.TryGetMember("size", out _));
        Assert.False(store.TryCallMethod(null, "get", out _));
        Assert.False(store.Invoke(new CallContext(new Interpreter(), "nope", new List<object>()), "nope", out _));
        Assert.Contains("isn't a valid store", Assert.Throws<KScriptException>(() => Run("<echo>{ @cache('redis') }</echo>")).Message);
        Assert.Equal("memory memory\n", Run("<echo>{ @cache().kind } { @cache(memory, name: 'x').kind }</echo>"));
        Assert.NotEmpty(new CachePackage().Description);
    }

    protected override void OnDispose() => CacheStore.Clock = () => DateTime.UtcNow;
}
