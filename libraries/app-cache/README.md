# App cache

Install the cache helpers used in KScript's API and fullstack project templates.

```sh
kscript lib add app-cache
```

From a script in the project root, give `cached` a key and a function. It runs the function on a cache miss. Call `forget_cached` when the underlying value changes.

```kscript
import "lib/app-cache/index.kscript" as cache

answer = @cache.cached('answer', fn() => 6 * 7)
echo $answer
@cache.forget_cached('answer')
```

The library uses the `app` store in `KScript.Cache`. It keeps values in memory for five minutes by default. Set `cache.store` to `file` in the `config` section of your application's `kscript.json` to keep values between runs, and set `cache.ttl` to change their lifetime. Call `@cache.app_cache()` to access the configured store directly.
