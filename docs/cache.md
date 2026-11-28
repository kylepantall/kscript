# Caching: KScript.Cache

## Introduction

Some work is slow: a web request, reading a big file, a long calculation. When its answer doesn't change every time, you may keep the answer and reuse it. KScript.Cache keeps values by key, for as long as you say, so the slow work is done once.

To get started, import the package and use `$cache`:

```kscript
import KScript.Cache
import KScript.Net

repos = $cache->remember('repos', fn() => @api('https://api.example.com/repos')->json(), ttl: 10m)
echo "{ $repos->count() } repos"
```

`remember` returns the value kept under the key. When there isn't one, or it has run out, it calls the function, keeps what it returns for ten minutes, and returns that.

## Stores

A store is where cached values live. KScript.Cache has two.

`$cache` keeps values in memory, for as long as kscript runs. In `kscript serve`, every request shares it, so a value one request keeps is there for the next:

```kscript
import KScript.Cache

$cache->set('greeting', 'Hello', ttl: 1h)
echo $cache->get('greeting')
```

`@cache(file)` keeps each value in a file in the app's `cache` folder (see [App folders](storage.md)), so values last between runs. Values are kept as JSON, so a file store holds maps, lists, text, numbers and true or false:

```kscript
import KScript.Cache

weather = @cache(file)
$weather->set('london', { high: 14, low: 6 }, ttl: 3h)
echo $weather->get('london').high
```

Give a store a name to keep its keys apart from other stores': `@cache(file, name: 'weather')`. Stores also belong to their app, so two apps never see each other's keys. `@cache(memory)` is the same store as `$cache`.

## Using the cache

### Reading values

`->get()` returns the value under a key. When the key isn't there, or its value has run out, you get `null`, or the default you give:

```kscript
import KScript.Cache

theme = $cache->get('theme', default: 'light')
echo $theme
```

`->has()` tells you whether a value is there without reading it, `->ttl()` says how long it has left as a duration (`null` when it never runs out), and `->keys()` lists every key, sorted:

```kscript
import KScript.Cache

$cache->set('token', 'abc123', ttl: 15m)
if $cache->has('token') {
  echo "The token runs out in { $cache->ttl('token') }"
}
echo $cache->keys()
```

### Storing values

`->set()` keeps a value and returns it. `->set_if_absent()` keeps it only when the key isn't there yet (or has run out), and returns true when this call stored it, which makes it a simple lock:

```kscript
import KScript.Cache

$cache->set('last_run', @now())
if $cache->set_if_absent('report_running', true, ttl: 5m) {
  echo 'Building the report'
}
```

### Remembering

Most of the time you want `->remember()`: read the value, or work it out and keep it when it isn't there. The function only runs on a miss:

```kscript
import KScript.Cache

fn slow_total() => [1, 2, 3, 4]->sum()

total = $cache->remember('total', fn() => @slow_total(), ttl: 1h)
echo $total
```

### Counting

`->increment()` adds to a number and returns the new one. A key that isn't there starts at 0. Adding is one step, so requests and scripts that count at the same moment all count. A count keeps the time limit it started with unless you give a new one:

```kscript
import KScript.Cache

store = @cache(file, name: 'visits')
count = $store->increment('today', ttl: 1d)
$store->increment('downloads', by: 5)
echo "Visit { $count } today"
```

### Removing values

`->remove()` removes one key and returns true if it was there. `->clear()` removes every key and returns how many there were:

```kscript
import KScript.Cache

$cache->set('draft', 'Dear Joan')
$cache->remove('draft')
echo "Cleared { $cache->clear() } values"
```

### Groups

A group is a small cache of its own inside a store, with the same methods. Its keys are kept apart from the store's own and from other groups', and `->clear_group()` removes all of them at once:

```kscript
import KScript.Cache

rates = $cache->group('rates')
$rates->set('GBP', 1.0)
$rates->set('EUR', 1.17)
echo "Removed { $cache->clear_group('rates') } rates"
```

## Time limits

`ttl` is how long a value is kept, written as a duration: `500ms`, `30s`, `10m`, `2h`, `1d` or `1w`, joined for more (`1h30m`). A plain number is seconds, so `ttl: 90` is a minute and a half. Without a `ttl`, a value is kept until you remove it or clear the store.

## HTTP caching

KScript.Http uses KScript.Cache for HTTP caching. A route may tag its answers with an ETag, answer `304 Not Modified` when the browser already has the latest answer, set `Cache-Control`, and keep whole answers in the memory cache:

```kscript
import KScript.Http
import "controllers/people.kscript" as people

Routes {
  Get(path: /people, to: @people.index, etag: true, cache: 60)
  Get(path: /stats, to: @people.stats, middleware: "cache_response:5m")
}
```

See [Caching and ETags](http.md#caching-and-etags) for the details.
