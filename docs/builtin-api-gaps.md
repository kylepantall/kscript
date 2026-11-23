# Built-in API gaps

This document tracks the built-in API work and the contracts that guided it. The tables under each section record the original gaps. The following APIs are included in this worktree.

| Area | Built-in APIs |
| --- | --- |
| Routing | `@routes(kind?, data_type?, locale?)`, `@route_match(path, method?)`, `@route_is(name_or_pattern)`, `$request.route_info`, and `kscript routes --output json` |
| Web | Route `label_key:` and typed `data:` options, file-based page label metadata, `@asset_url(path)`, and `@sitemap(paths, origin)` |
| HTTP | `@http_client(base_url, options?)`, `@api` retry options, `@parse_query(text)`, scoped `@with_http_stub(handler, work)`, and `response.bytes` |
| Collections | List `flat_map`, `partition`, `unique_by`, and `find_last`. Map `pick`, `omit`, and `deep_merge`. |
| Text and dates | `strip_prefix`, `strip_suffix`, `normalize_unicode`, and `date->duration_since(other)` |
| File I/O | `@read_bytes`, `@write_bytes`, `@read_csv`, `@write_csv`, `@write_atomic`, `@with_temp_file`, and `@with_temp_folder` |
| Data | `cursor_paginate`, `upsert`, transactional bulk `insert`, and `database->describe_table(table)` |
| Cache | `set_if_absent`, `group`, and `clear_group` |
| Testing | `@with_clock`, `@with_environment`, and `@assert_response` |

## First priority: route and page discovery

`kscript routes -v` prints declared HTTP routes and their source locations. `@route(name, ...)` builds a path from a known route name. The OpenAPI export describes the API routes selected by its configuration. Before `@routes()`, scripts had to gather those sources separately. This makes it harder to build a sitemap, generate navigation, check links, or test the shape of a project's routing table. See the [HTTP guide](http.md), [web guide](web.md), and the [route listing implementation](../KScript.Http/HttpServer.cs).

### Route records from `@routes()`

Return structured route records for the current project. A page can filter these records for navigation, and a test can assert that a path is registered.

```text
pages = @routes(kind: 'page')
post = $pages->find(fn(entry) => $entry.path == '/posts/{slug}')
echo $post.parameters                  // ['slug']
```

| Field | Meaning |
| --- | --- |
| `kind` | `'page'` for file-based pages or `'http'` for an explicit declaration. |
| `path` | Canonical URL pattern. For example, `index.html` produces `/`, and `posts/[slug].md` produces `/posts/{slug}`. Dynamic patterns have no concrete URL until their parameters are supplied. |
| `methods` | HTTP methods explicitly accepted by the entry, including implied HEAD for a declared GET. Any-method routes and file-based pages have an empty `methods` list and `any_method: true`. |
| `name` | Configured route name, or `null`. |
| `parameters` | Path parameter names in path order. |
| `label_key`, `label` | Optional translation key and its resolved display text. See [Localized route labels](#localized-route-labels). |
| `data` | Optional record made from a declared type. See [Typed route data](#typed-route-data). |

- Default to both kinds. Support `kind: 'page'` and `kind: 'http'` so scripts can ask for one. Sites with no explicit route files still return their file-based pages.
- Use the same page exclusions and precedence as the live resolver. Keep one canonical entry for each page. Respect reserved directories, configured views, static assets, explicit route ownership, and HTML versus Markdown precedence. Represent dynamic patterns without inventing parameter values. The listing uses canonical paths without locale prefixes. A dynamic pattern remains in the listing when an explicit route shadows only some of its URLs.
- Read route metadata from the current app's cached or in-flight route table. Listing from a request must not render a page, dispatch a controller, call middleware, or recursively load the route files. Define an outside-request path for project scripts and tests that follows the existing route-loading rules.
- Keep introspection separate from authorization. A route listing can include protected patterns, so public navigation or sitemap code must choose what to expose. Leave handler internals and filesystem locations to development tooling.
- `kscript routes --output json` prints the same record layout and keeps app-specific data out of the export. The terminal table remains the default.
- Test file-only sites, dynamic folders, index pages, duplicate aliases, hidden templates, explicit routes that own a page, grouped and resource routes, and method handling. Add a rendered HTML example that uses `<for>` to consume the result.

Implementation starting points are [RouteTable](../KScript.Http/Routing.cs), the cached [HttpApp table](../KScript.Http/HttpApp.cs), and [PageSite.Resolve](../KScript.Web/Pages/PageServer.cs).

### Localized route labels

A named route has a stable identifier such as `home`, used by `@route('home')`. The route declaration has no field for a reader-facing label such as **Home** or **Maison**. Today a template can call `@t('routes.home')`, but it has to know the translation key itself. Markdown front matter can supply a page title, which is separate from route metadata.

Add an optional `label_key` to the route declaration. Resolve it through the existing translation catalog when listing or presenting routes.

```text
Get(path: "/", view: "home.html", name: "home", label_key: "routes.home")
```

| Translation file | Entry |
| --- | --- |
| `locales/en.json` | `{ "routes": { "home": "Home" } }` |
| `locales/fr.json` | `{ "routes": { "home": "Maison" } }` |

- `@routes()` resolves labels using the current locale. The optional `locale:` argument lets a script build navigation for another locale.
- Use the configured fallback locale for a missing translation. Return `null` for the display label when the route has no key or no translation exists in the fallback chain. Keep the key available for diagnosis.
- Keep the route name and path stable across languages. Build localized links with the existing `@locale_path()`. A Markdown page can set `label_key` in its front matter. In HTML, use `<meta name="kscript:label-key" content="routes.about">` near the top of the page.
- Verify the same route produces **Home** and **Maison** from the two catalogs. Cover locale fallback, unnamed routes with a label, and HTML-escaped labels in a rendered navigation list.

See [Translations in the language guide](language.md#translations-t) and the existing [HTTP route attributes](../KScript.Http/HttpPackage.cs).

### Typed route data

A route can optionally carry app-specific data for navigation, breadcrumbs, or sitemaps. Give that data a declared record type so required fields and their types can be checked where the route is written.

Define the type once in `types/navigation-data.kscript`.

```text
type NavigationData {
  order: int
  icon: string
}
```

Import it into the route file and pass a record constructed from it to the `data:` argument. The constructor gives the editor the exact fields and types at the call site.

```text
import KScript.Http
import "../types/navigation-data.kscript"

Routes {
  Get(
    path: "/home",
    name: 'home',
    view: 'home.html',
    label_key: 'routes.home',
    data: NavigationData(order: 10, icon: 'home')
  )
}
```

A page imports the same declaration. The `data_type:` filter selects routes carrying that type and gives the loop a typed `$entry.data`. The template can sort links by the declared `order` and use the translated label.

```html
<import package="KScript.Http" />
<import path="types/navigation-data.kscript" />

<nav aria-label="Main navigation">
  <ul>
    <for each="entry" in="@routes(data_type: NavigationData)->sort_by(fn(r) => $r.data.order)">
      <li>
        <a href="{ @route($entry.name) }"
           data-icon="{ $entry.data.icon }">{ $entry.label }</a>
      </li>
    </for>
  </ul>
</nav>
```

- The route can omit `data`. When supplied, it must be a value of a declared record type. The constructor applies that type's existing validation rules. Routes with no attached data have `data: null`.
- Store the type identity with the record. A typed listing filters by declaration, so a record with the same fields from another type does not accidentally appear in the menu. Completion should offer the declared fields on `$entry.data`, including in HTML.
- The illustrated menu uses named routes without path parameters. A dynamic route needs concrete parameter values before it can become a link. Attaching data has no effect on authorization.
- Keep app-specific route data out of public exports by default. Test a missing type, a missing required field, two different record types with the same fields, and an empty typed listing.
- The equivalent XML argument is `data="NavigationData(order: 10, icon: 'home')"`. Both forms use the same constructor. KScript already uses square brackets for indexing, such as `$data[key]`.

### Type arguments and constraints

Generalize the existing type-as-value pattern from calls such as `->json(as: Repo)`. A method should be able to require a type argument, constrain which types it accepts, and use that argument to check a companion value or describe its result. Requiring a declared record for route data is one use of that design.

| Proposed constraint | What it would guarantee |
| --- | --- |
| `Record` | The caller supplies a declared record type. The editor can suggest its fields, catch misspelled keys, and report missing required fields. Runtime checks also cover values built dynamically. |
| `Array` | The caller supplies a KScript list type, optionally with a known element type. Use this when an API specifically needs list ordering, indexing, or list methods. |
| `Iterable` | The value can be traversed. KScript's `for` already accepts several kinds, including lists, maps, ranges, and text. Specify what an iteration yields for every kind the constraint admits. |
| `Mappable` | The value has a mapping contract. Define callback arguments and result shape before admitting multiple kinds. Start with `Array` when the method needs list mapping specifically. |

- Resolve a known type at the call site. The language server should complain at the type argument if the constraint fails, and at the offending field or value if the data fails its type check.
- Validate the type and constraint again at runtime when the caller gets a value dynamically. A valid type argument supplies a schema even when the analyzer cannot see the data.
- Keep type-level constraints reusable across built-in commands and methods. Decide the declaration syntax for user-defined functions separately after checking how type arguments flow into callbacks and return values.

## Web and HTTP

| Proposed API | Today | Gap |
| --- | --- | --- |
| Matched route metadata and `@route_is(name_or_pattern)` | A matched named HTTP route already exposes its name as `$request.route`. | Match an active navigation item by stable name or canonical pattern. Cover unnamed HTTP routes and file-based pages without changing the existing string field. |
| `@route_match(path, method: 'GET')` | The server matches routes internally. Calling `@api` dispatches a request. | Inspect a match without invoking the handler. Return parameters and distinguish no match from method mismatch. |
| `@asset_url(path)` | Templates write asset paths directly. The server serves compiled stylesheets at their public URL. | Provide one place to build asset URLs for an asset base path or a versioned manifest. |
| `@parse_query(text)` | `@query_string(map)` encodes a query. `url.query` and `$request.query` expose parsed maps. | Parse standalone query strings with an explicit contract for repeated keys, empty values, and malformed encoding. |
| `@sitemap()` or a sitemap library | Pages and handlers can write XML responses. | Generate sitemap entries from an explicit public selection of routes plus any dynamic URLs supplied by the app. Build on `@routes()` once it exists. |
| Reuse fixed callable signatures | HTTP middleware uses the shared `fn(request: map, next: fn)` contract for route loading and editor checks. | Apply the same metadata to other arguments with a fixed calling convention as their contracts are defined. Plain `fn` callbacks can continue accepting any callable. |
| HTTP client defaults and retry policy | `@api(url, options: ...)` accepts per-call options. `response->check()` raises for a failed response. | Reuse a base URL, headers, timeouts, and cancellation policy across calls. Make retries opt-in and document which methods and failures qualify. |
| Outbound HTTP stubs for tests | `@api('/path')` can call project routes in-process. | Let unit tests supply responses or network failures for external URLs without opening a connection. Keep stubs scoped to the test. |

## Collections and text

| Proposed API | Today | Gap |
| --- | --- | --- |
| `list->flat_map(fn)` | `->map(fn)->flatten()` | Flatten exactly one level with a single operation and define how it handles typed lists. |
| `list->partition(fn)` | `->filter(fn)` and `->reject(fn)` | Split into matching and remaining lists with one predicate call per item. |
| `list->unique_by(fn)` | `->unique()` uses strict equality. `->group_by(fn)` groups keys. | Keep the first item for each derived key while preserving order. Specify equality for derived keys. |
| `list->find_last(fn)` | `->find(fn)` and `->reverse()` | Search backward without building a reversed copy. Give callbacks the original index. |
| `map->pick(keys)`, `map->omit(keys)` | `->filter(fn)`; list `->pluck(keys)` projects fields from each item. | Project or drop a set of keys from a single map. Useful for view data and request payloads. |
| `map->deep_merge(other)` | `->merge(other)` combines top-level keys. | Merge nested config while documenting array replacement, null values, and key precedence. |
| `string->strip_prefix(text)`, `string->strip_suffix(text)` | `starts_with`, `ends_with`, and `slice`. | Remove one exact prefix or suffix without trimming repeated characters or replacing text elsewhere. |
| `string->normalize_unicode(form?)` | `@slugify()` produces a slug. Case and whitespace normalization are separate operations. | Normalize equivalent Unicode spellings without changing case, dropping accents, or generating a slug. |
| Date difference as a duration | Subtracting two dates returns a number of days. Duration values have `->after()` and `->before()`. | Add a typed duration result with a clear sign and time-zone contract. Keep date subtraction compatible. |

## Files, data, cache, and tests

| Proposed API | Today | Gap |
| --- | --- | --- |
| Binary file read and write | `@read_text()`, `@write_text()`, and Base64 text helpers. `@download()` can save a remote file. | Read and write byte-preserving values. Share the representation with response bodies and encoders instead of treating arbitrary bytes as Unicode. |
| CSV read and write | IO has JSON, YAML and line readers. `->split(',')` only handles simple rows. | Round-trip quoted delimiters, escaped quotes, and embedded newlines. Set a header and typing contract. |
| `@with_temp_file(fn)` and `@with_temp_folder(fn)` | `@temp_folder()`, `@create_folder()`, and `@delete()`. | Allocate a unique temporary path and clean it up even when the callback throws. |
| Atomic replacement for arbitrary IO paths | `storage->write()` safely replaces files in application storage. | Provide the same explicit guarantee for general IO paths and keep existing path checks. |
| `query->cursor_paginate(...)` | `->paginate()` supplies offset pages and a total. | Traverse large changing datasets using a stable sort and cursor, without counting all matching rows. |
| Atomic upsert and bulk insert | Models offer `->create()` and `->first_or_create()`. Queries can run raw parameterized SQL. | Expose unique-key conflict handling and multi-row writes with defined validation, transaction, and cross-provider behavior. |
| `database->columns(table)` or a schema description method | `->tables()`, `->has_table()` and CLI migration/schema tooling. | Inspect column types, nullability, defaults, indexes and keys without provider-specific SQL. |
| `cache->set_if_absent(key, value, ttl: ...)` | `->has()`, `->set()`, `->remember()`, and `->increment()`. | Reserve a key in one operation and specify the guarantee for both memory and file stores. |
| Cache invalidation by group | `->keys()`, `->remove()`, and `->clear()`. | Expire related entries without scanning every key or clearing unrelated entries. |
| Clock and environment overrides for tests | Core exposes `@now()`, `@today()`, and `@env()`. Queue tests can use `@fake_queue()`. | Exercise time-dependent and configuration-dependent branches with scoped overrides and automatic restoration. |
| HTTP response assertions for tests | `expect` can inspect status, headers, and JSON as individual values. | Provide reusable assertions for status, redirects, headers, and typed JSON with failure messages that point to the field that differed. |

## Existing APIs to surface in the reference

- Use `string->escape()` to HTML-escape text outside a template. Template interpolation already escapes it.
- Use `@slugify()` to create a slug, and use `url->join()` and `url->with_query()` to build on an existing URL.
- Use `@files(folder, pattern: ..., recursive: true)` for recursive wildcard file listing. A future glob API would need to support more than this.
- Use `@t()` for translation and `@locale_path()` for localized links. Route labels can build on both.

## Behavior to verify

- HTTP retries share one timeout budget. A retry is allowed for GET, HEAD and OPTIONS on a connection failure or status 408, 429, 502, 503, or 504. The HTTP stub is restored when its callback returns or throws.
- Byte APIs exchange `base64` values. CSV handles quoted cells and multiline fields. Header rows produce maps, which can be validated with `as:`.
- Cache reservation replaces expired entries. Named groups use their own namespace. Clearing a group leaves other groups and ungrouped entries alone.
- Cursor pagination requires a unique, non-null column. It returns `next` only if another page exists. Bulk inserts commit together or roll back together. Upsert returns the database provider's affected-row count.
- Clock and environment overrides are scoped to `@now()`, `@today()`, `@env()`, and `$env` in the test callback.
- The [language regression script](../samples/scripts/tests/language/builtin-api.test.kscript) covers collection, file, HTTP, cache, and test APIs. The [data sample test](../samples/data/tests/product.test.kscript) exercises upsert, cursor pagination, and table descriptions.
