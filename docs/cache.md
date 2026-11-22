# Caching: KScript.Cache

KScript.Cache keeps values by key for as long as you say, so slow work (a web request, a big file, a long calculation) is done once and reused. `$cache` keeps values in memory. `@cache(file)` keeps them in files in the app's cache folder, so they last between runs.

```kscript
import KScript.Cache
import KScript.Net

repos = $cache->remember('repos', fn() => @api('https://api.example.com/repos')->json(), ttl: 10m)
echo "{ $repos->count() } repos"
```

`remember` returns the value kept under the key. When there isn't one, or it has run out, it calls the function, keeps what it returns, and returns that.

## Stores

| Store | Where values live | How long |
| --- | --- | --- |
| `$cache` or `@cache(memory)` | In memory | While kscript runs. In `kscript serve` that means every request shares it. |
| `@cache(file)` | One file per key in the app's `cache` folder (see [App folders](storage.md)) | Until it runs out, across runs. |

A name keeps a store's keys apart from other stores': `@cache(file, name: 'weather')`. Stores are per app, so two apps never see each other's keys. File values are kept as JSON, so keep maps, lists, text, numbers and true/false in a file store.

## Methods

| Method | What it does |
| --- | --- |
| `->get(key, default: …)` | The value, or the default (`null`) when it isn't there or has run out. |
| `->set(key, value, ttl: …)` | Keeps the value and returns it. |
| `->remember(key, compute, ttl: …)` | The value, or else what `compute` returns, which is then kept. |
| `->has(key)` | True when a value is there and hasn't run out. |
| `->remove(key)` | Removes the key. True if it was there. |
| `->increment(key, by: 1, ttl: …)` | Adds to a number (0 when it isn't there) and returns the new number. A count keeps the time limit it started with unless you give a new one. |
| `->ttl(key)` | How long until the key runs out, as a duration, or `null` when it never does. |
| `->keys()` | The keys that are there, sorted. |
| `->clear()` | Removes every key and returns how many there were. |

## Time limits

`ttl` is how long a value is kept, written as a duration: `500ms`, `30s`, `10m`, `2h`, `1d` or `1w`, and joined for more (`1h30m`). A plain number is seconds (`ttl: 90`). Without a `ttl` a value is kept until it is removed or the store is cleared.

```kscript
import KScript.Cache

store = @cache(file, name: 'visits')
count = $store->increment('today', ttl: 1d)
echo "Visit $count today, { $store->ttl('today') } left"
```

## In routes: ETags and Cache-Control

KScript.Http uses KScript.Cache for HTTP caching. A route can tag its answers with an ETag, answer `304 Not Modified` when the browser already has them, set `Cache-Control`, and keep whole answers in the memory cache:

```kscript
import KScript.Http
import "controllers/people.kscript" as people

Routes {
  Get(path: /people, to: @people.index, etag: true, cache: 60)
  Get(path: /stats, to: @people.stats, middleware: "cache_response:5m")
}
```

See [Caching and ETags](http.md#caching-and-etags) for the details.
