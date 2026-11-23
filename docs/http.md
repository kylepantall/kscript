# Routes, controllers and middleware: KScript.Http

KScript.Http gives `kscript serve` a server framework in the spirit of Laravel's `routes/web.php` and `routes/api.php`. Route files map HTTP methods and paths to controllers. Controllers are ordinary `fn` functions that return a view, JSON or a response. Middleware runs around them for logging, CORS, auth, rate limits or your own checks.

The page features you already have keep working. `kscript serve` sends each request to the routes first. Anything no route matches goes on to the site's pages and files.

```sh
kscript serve samples/http-app      # then open http://localhost:8080/people
kscript routes samples/http-app     # print the route table
kscript openapi samples/http-app    # the OpenAPI document, made from the routes
```

[`samples/http-app`](../samples/http-app) is a complete site: web routes, API routes, a controller, views, custom middleware and a `kscript.json`.

## Route files

A file that imports KScript.Http and holds a `Routes { }` block (`<KScript.Routes>` in XML) is a route file, the same way `Window { }` makes a desktop app. Its imports go at the top of the file, above the block. `kscript serve` loads these files from the site folder:

| File | Routes |
| --- | --- |
| `routes/web.kscript`, or `routes.kscript` at the top (or `.ks.xml`) | Web routes, mounted at `/`. |
| `routes/api.kscript`, or `api.kscript` at the top (or `.ks.xml`) | API routes, mounted under `/api`. Errors and missing routes are answered with JSON. |

`http.routes` in `kscript.json` can list other files instead (see [Settings](#settings-kscriptjson)). `kscript make route users` adds a route and makes its controller (see [Generating files](projects.md#generating-files-kscript-make)): `kscript make route "people/{id}" --api`, `kscript make route people --resource`, and `kscript make controller`, `request`, `middleware` and `view` for the rest.

```kscript
// routes.kscript
import KScript.Http
import "controllers/people.kscript" as people
import "middleware/security.kscript"

Routes {
  Middleware(use: secure_headers)

  Get(path: /, view: "home", name: "home")
  Get(path: /people, to: @people.list, name: "people.list")
  Get(path: /people/{id}, to: @people.page, name: "people.page", where: "id: int")
  Get(path: /team, redirect: "/people", status: 301)

  Get(path: /ping) {
    return 'pong'
  }
}
```

| Element | Use |
| --- | --- |
| `Get` `Post` `Put` `Patch` `Delete` `Options` `Any` | A route for that method. `Get` answers `HEAD` too. |
| `Route(methods: [get, post])` | A route for several methods. |
| `Resource(path: /people, to: people)` | The usual JSON resource routes, answered by the controller's `index`, `store`, `show`, `update` and `destroy` functions (see [Resources](#resources)). |
| `Group(prefix, middleware, name)` | Routes that share a path prefix, middleware or a name prefix. Groups nest. |
| `Middleware(use: [log, cors])` | Middleware for the routes after it in this file or group. |

A route has a `path` and one way to answer:

| Attribute | Use |
| --- | --- |
| `path` | `/people/{id}`. `{name?}` is optional and `{name*}` takes the rest of the path (`/files/{path*}`). A path is a [slash path](types.md#slash-paths), so it needs no quotes: `path: /people/{id}` and `path: "/people/{id}"` are the same. Either way `{id}` is a route parameter, never filled in from a variable. |
| `to` | The controller: a function of an imported script (`@people.show`) or an `fn` in the route file (`@show`). In XML: `to="people.show"`. |
| `view` | Render this view with the path parameters as its variables. |
| `redirect`, `status` | Redirect, with 302 by default. |
| a `{ }` block | The route's own body, like an `fn`: `Get(path: /ping) { return 'pong' }`. |
| `name` | A name for `@route('people.show', id: 3)`. Names must be unique. |
| `middleware` | Middleware for this route only. |
| `where` | Rules for parameters: `where="id: int, slug: slug"`. The rules are `number`, `int`, `alpha`, `alnum`, `slug`, `uuid`, or a regular expression in quotes. A request that breaks a rule doesn't match the route. |

Routes are tried in the order they are written, and the first match wins. A path that matches but has no route for the method gets `405 Method Not Allowed` with an `Allow` header. An `OPTIONS` request gets `204` with `Allow`, which is how CORS preflights are answered (see [cors](#built-in-middleware)).

The route file runs on every request, before the route is matched. So it can use `if` to add routes only in some cases, and its imports are new for each request, just like pages.

## Controllers

A controller is a plain `fn`. Its parameters are filled in by name:

- Path parameters: `{id}` becomes `$id`.
- `request` contains the [page request fields](web.md), including `scheme` and `url`. A matched route also supplies `ip`, `json`, `input` and `route` (its name, or `null`).
- `json`: the body read as JSON (`null` if it wasn't JSON).
- `body`: the JSON if there was any, otherwise the raw text.
- `input`: the query, form and JSON fields together.
- `query`, `form`, `headers`, `cookies`.

A typed parameter is converted, so `id: int` gives a number. A `type` maps the request data into a record, so the controller only sees data of the right shape:

```kscript
type NewPerson {
  name: string
  age?: int
  role?: string
}

fn store(json: NewPerson) {
  // $json is a NewPerson record
  // …
}
```

A path parameter typed as a [KScript.Data](data.md) model is the record with that key, so the controller gets the post itself. A key with no record, or one that isn't a key, gets `404`, and OpenAPI shows the key's type:

```kscript
// routes: Get(path: /posts/{post}, to: @posts.show)
fn show(post: Post) => $post
```

Data that doesn't fit is the client's mistake, so the controller doesn't run. A body, query or form that doesn't fit gets `422 Unprocessable Entity`. API routes answer with JSON that lists every problem: `{ "error": "...", "status": 422, "problems": [{ "path": "$.age", "field": "age", "message": "..." }] }`. A path parameter that doesn't fit, such as `/people/abc` for `id: int`, gets `404`.

```kscript
// controllers/people.kscript
kscript {
  import KScript.IO
  import KScript.Http

  fn all() => @read_json('data/people.json')
  fn find(id: int) => @all()->find(fn(p) => $p.id == $id)

  fn index(query: map) {
    // GET /api/people?role=Founder
    return @all() |> filter(fn(p) => $query.role == null or $p.role == $query.role)
  }

  fn page(id: int) {
    // GET /people/2
    person = @find($id) ?? @abort(404, message: 'Nobody has that id.')
    return @view('people/show', person: $person)
  }

  type NewPerson {
    name: string
    age?: int
    role?: string
  }

  fn store(json: NewPerson) {
    // POST /api/people
    if $json.name->trim() == '' {
      return @response({ error: 'A person needs a name.' }, status: 422)
    }
    // …
    return @response($person, status: 201, headers: { Location: @route('people.show', id: $person.id) })
  }
}
```

What a controller returns becomes the response:

| Return | Response |
| --- | --- |
| A map, list, number or true/false | JSON (`application/json`). |
| Text | HTML. |
| `@view('people/show', person: $p)` | The view rendered with its layout. |
| `@response(body, status: 201, type: 'text/csv', headers: { … })` | Your own status, type and headers. A map or list body is JSON. |
| `@redirect(/people)`, `@redirect(to: '/new', status: 301)` | A redirect. |
| nothing (`null`) | `204 No Content`. |

`@abort(404)` or `@abort(403, message: 'Admins only')` stops the request with that status, from anywhere: a controller, a function it calls, or middleware. `try` doesn't catch it. An error in a controller answers `500` with the file and line, and the server log shows it too.

### Errors and status codes

An [error type](language.md#error-types) can carry a status. When a controller (or anything it calls) throws one and nothing catches it, the request is answered with that status and the error's message, without the file and line. API routes answer JSON with the error's type and fields as well:

```kscript
error PersonNotFound(message: "No person with id {$id}") extends NotFound {
  id: int
}

people = { 1: { name: "Ada" } }

fn show(id: int) {
  person = $people[$id]
  if $person == null {
    throw PersonNotFound(id: $id) // GET /api/people/7 answers 404: { "error": "No person with id 7", "status": 404, "type": "PersonNotFound", "id": 7 }
  }
  return $person
}
```

`Validation` (422), `BadRequest` (400), `Unauthorized` (401), `Forbidden` (403), `NotFound` (404), `Conflict` (409) and `TooManyRequests` (429, with `Retry-After` when the error has a `retry_after` field) are built in, so `throw Forbidden "Admins only"` works without a declaration, and an error type that extends one answers with its status. Give an error type its own status with `error PaymentFailed(status: 402)`. Unlike `@abort()`, these are ordinary errors: a `try` in the controller can still catch them. Errors without a status still answer `500`.

Controllers can read and write files with KScript.IO, but only inside the site folder. Set `"write_files": false` to make them read-only like pages. Anything a controller prints with `echo` goes to the server log.

## Views

`@view('people/show', person: $p)` renders `views/people/show.html` (or `.md`). If that file doesn't exist, it renders `people/show.html` in the site folder. Leaving off the extension is fine. The named values become the view's variables, and `$request` is there too. Views are normal pages, so layouts, components, `<include>` and `<live>` all work in them.

The `views` folder is never served as pages, because a view needs its controller's data. The editor knows that views get their variables from a controller, so it doesn't flag them as undefined. It also reads every `@view('people/show', ...)` in the project's scripts, so in `views/people/show.html` typing `$` offers `$person`, `$person.` offers the fields the controller gave it, and a loop over a list it was given knows the fields of each item. Hovering `$person` says which controller passes it, and **Go to Definition** opens that call. A view that names a package with `<import package="KScript.Auth" />` gets its variables too, so `$session->` lists the session's methods.

## Middleware

Middleware is a function that takes the request and `$next`. It can stop the request by returning a response, or call `$next($request)` and change the response it gets back:

```kscript
// middleware/security.kscript
kscript {
  import KScript.Http

  fn secure_headers(request: map, next: fn) {
    return $next($request)->with_header('X-Frame-Options', 'DENY')
  }

  fn json_only(request: map, next: fn) {
    if $request.method == 'POST' and not ($request.headers['content-type'] ?? '')->contains('json') {
      return @response({ error: 'Send JSON.' }, status: 415)
    }
    return $next($request)
  }
}
```

Middleware applies at four levels, and it runs outermost first:

1. `http.middleware` in `kscript.json`, for every route.
2. `Routes(middleware: …)`, for every route in the file. `Middleware(use: …)` adds more for the routes after it.
3. `Group(middleware: …)`, for the routes inside the group.
4. `Get(…, middleware: …)`, for one route.

A middleware list is an array of names: `[log, secure_headers, throttle:30]`. Each entry can be a built-in, a function (including one from an imported script), or a group name from `http.groups`. Built-ins take an argument after a colon, such as `throttle:30`. A single entry needs no brackets: `middleware: auth`.

```kscript
// routes.kscript
import KScript.Http
import "middleware/security.kscript"

Routes {
  Middleware(use: [secure_headers, json_only])
}
```

In XML, attributes are text, so the same list is written `use="secure_headers, json_only"`. In `.kscript` files, a list written as one string (`use: "log, cors"`) is an error (KS6006), and `kscript check --fix` rewrites it as the array. The same goes for every list of names: `only`, `except`, `methods`, and `<llm tools>`.

### Built-in middleware

| Name | What it does |
| --- | --- |
| `log` | Writes each request to the server log: what answered it, the route name, the status and the time. |
| `cors` | Answers browser preflight requests and adds `Access-Control-Allow-Origin`. The allowed origins, methods, headers and credentials come from `http.cors`. |
| `auth` | Lets through a user signed in with [KScript.Auth](auth.md) (by the session cookie or an API token), and sets `$request.user`. It also accepts `Authorization: Bearer <token>` matching the `API_TOKEN` environment variable (`http.auth.token_env` names another). Anyone else is sent to `http.auth.login` (`/login`) in a browser, or gets a 401 when they want JSON. `auth:session` and `auth:token` accept only one kind of sign-in. `auth:ADMIN_TOKEN` accepts only the token in that variable, and refuses every request (500) when it isn't set. Requests that pass have `$request.authenticated` set. |
| `guest` | Only people who aren't signed in: others are sent to `http.auth.home` (`/`), or get a 403 when they want JSON. For the sign-in and sign-up pages. |
| `csrf` | Needs the session's CSRF token on every request except `GET`, `HEAD` and `OPTIONS`: a `_token` form field or an `X-CSRF-Token` header. Without it, `419 Page Expired`. Web routes in a file that imports KScript.Auth get it on their own (see [Forms and CSRF](auth.md#forms-and-csrf)). |
| `throttle` | Allows 60 requests a minute per client address and route (`http.throttle`), or `throttle:30`. Over the limit gets `429` with `Retry-After`. Every response has `X-RateLimit-Limit` and `X-RateLimit-Remaining`. |
| `etag` | Tags `GET` answers with an ETag and answers `If-None-Match` with `304 Not Modified` (see [Caching and ETags](#caching-and-etags)). `etag:weak` gives weak tags. |
| `cache_control` | Sets `Cache-Control` on `GET` answers: `cache_control:300` (public for five minutes) or `cache_control:private;max-age=60`. |
| `cache_response` | Keeps whole `GET` answers in memory and answers again from there: `cache_response:5m` (one minute by default). |

A function with the same name as a built-in wins, so you can replace one.

## Caching and ETags

Browsers and proxies can keep an answer and ask again only when it may have changed. KScript.Http helps at three levels, with [KScript.Cache](cache.md) underneath.

**ETags.** `etag: true` on a route (or the `etag` middleware) adds an `ETag` header to every `200` answer to `GET` and `HEAD`: a hash of the body. When the browser asks again with `If-None-Match` and the tag still matches, it gets `304 Not Modified` with no body, so nothing is sent twice.

```kscript
import KScript.Http
import "controllers/people.kscript" as people

Routes {
  Get(path: /people, to: @people.index, etag: true, cache: 60)
  Get(path: /people/{id}, to: @people.show, etag: true, cache: "private, max-age=0, must-revalidate")
}
```

The route still runs before its body is hashed. To skip the work as well, a controller can name its version itself with `@etag(value)`. The value can be anything that changes when the answer would, such as the record or its `updated_at`. When the browser already has that version, the request stops right there with `304`:

```kscript
kscript {
  import KScript.Http
  import KScript.IO

  fn show(id: int) {
    person = @read_json('data/people.json')->find(fn(p) => $p.id == $id) ?? @abort(404)
    @etag($person.updated_at)
    return @view('people/show', person: $person)
  }
}
```

`@etag(value, weak: true)` makes a weak tag, and a response can be tagged by hand with `->with_etag()` (a hash of its body) or `->with_etag($value)`.

**Cache-Control.** `cache` on a route (or the `cache_control` middleware) sets `Cache-Control` on successful `GET` answers and on `304`s. A number alone is a public max-age in seconds, so `cache: 300` gives `public, max-age=300`. Otherwise list the directives: `public`, `private`, `no-cache`, `no-store`, `no-transform`, `must-revalidate`, `proxy-revalidate`, `must-understand`, `immutable`, `max-age=`, `s-maxage=`, `stale-while-revalidate=` and `stale-if-error=`. A directive the browser wouldn't understand is an error when the route file loads. A header set by the controller (`->with_cache('no-store')`) wins.

**Answers kept on the server.** `cache_response:5m` keeps whole `200` answers to `GET` in KScript.Cache's memory store for that long, by URL, and answers again from memory without running the controller. Answers carry `X-Cache: HIT` or `MISS`. Requests with an `Authorization` header, and answers marked `private` or `no-store`, are never kept, so one person's answer is never given to another.

Put `etag` in `http.middleware` (or in a group) to tag every route at once:

```jsonc
{ "http": { "middleware": ["log", "etag"] } }
```

## Resources

`Resource(path: /people, to: people)` adds these routes for the controller imported as `people`:

| Method | Path | Function | Name |
| --- | --- | --- | --- |
| GET | /people | `index` | people.index |
| POST | /people | `store` | people.store |
| GET | /people/{id} | `show` | people.show |
| PUT, PATCH | /people/{id} | `update` | people.update |
| DELETE | /people/{id} | `destroy` | people.destroy |

Functions the controller doesn't have get no route. `only: [index, show]` and `except: [destroy]` pick actions. `name` changes the name prefix, and `param` changes `{id}`. Put the write actions in an auth group:

```
Resource(path: /people, to: people, only: [index, show])
Group(middleware: auth) {
  Resource(path: /people, to: people, only: [store, update, destroy])
}
```

## The request

`$request` (or a controller's `request` parameter) is a map with these keys:

- `method` (upper case), `path`, `url` (path and query), `query`, `form`, `json`, `input`, `body` (the raw text).
- `headers`, with lower-case names, and `cookies`.
- `params` (the path parameters), `ip` and `route` (the route's name).

## Responses

`@response()`, `@view()` and `@redirect()` give a response value. Middleware gets one back from `$next($request)`.

`kscript serve` sends `Server: kscript` on HTTP responses. To set your own value for a route, use `->with_header('Server', 'my-app')`.

- Read it with `.status`, `.body`, `.type`, `.headers`, `->header(name)`, `.is_ok` and `->json()`.
- Change it with `->with_status(code)`, `->with_header(name, value)`, `->with_type(type)`, `->with_etag(value)` and `->with_cache(directives)`. Each returns the response, so the calls chain.

## Calling your own API

`@api()` ([KScript.Net](language.md)) with a path instead of an address asks the project's own routes, in the same process and without the network:

```kscript
import KScript.Net

notes = @api(/api/notes)->check()->json()
added = @api(/api/notes, options: { method: 'POST', body: { title: 'Ship it' } })
echo $added.status   // 201
```

It works from pages, tests and scripts in the project (the folder with `kscript.json`), and from routes. The options and the response are the same as for an address: `method`, `headers` and `body` (a map or list is sent as JSON), then `.status`, `->json()`, `->header(name)` and the rest. A path with no route is a 404.

`@api()` sends `User-Agent: kscript` by default for both remote and local requests. To identify a particular client, pass `options: { headers: { 'User-Agent': 'my-app' } }`. That override applies to one request.

A request made while answering another one, such as a page calling the API, or a route calling another route, sends on the `Cookie` and `Authorization` headers of the request being answered, so the API sees the same signed-in person. Give your own `headers` to send others. Requests inside one another stop after 8, so a route that calls itself is an error rather than a hang.

The [fullstack project template](projects.md#a-web-app-with-its-api---template-fullstack) uses this: its pages get their notes from `/api/notes`, so the pages and other clients share one API.

## Settings: kscript.json

```jsonc
{
  "http": {
    "routes": ["routes.kscript", { "file": "api/v1.kscript", "prefix": "/api/v1", "api": true }],  // default: routes/web.*, routes/api.*, routes.* and api.*
    "middleware": ["log"],                      // for every route
    "groups": { "api": ["cors", "throttle"] },  // names for lists of middleware
    "views": "views",                           // where @view() looks first; never served
    "api_prefix": "/api",                       // the prefix of api.* files
    "write_files": true,                        // false: controllers can only read files
    "cors": { "origins": ["*"], "methods": ["GET", "POST"], "headers": ["Content-Type", "Authorization"], "credentials": false },
    "auth": { "token_env": "API_TOKEN", "login": "/login", "lifetime": "2h" },  // and the KScript.Auth settings in auth.md
    "throttle": { "limit": 60, "seconds": 60 },
    "openapi": { "routes": "auto", "title": "People API", "version": "1.2.0", "file": "openapi.json" }  // or false
  }
}
```

## API docs: OpenAPI

The routes already say everything an API document needs, so KScript writes one for you. While `kscript serve` runs, open `/_api` for a page that lists every endpoint with its parameters, body and answers, plus a form to try each one. Tools such as Postman, client generators and gateways read the same document at `/_openapi.json` or `/_openapi.yaml`. `kscript openapi` writes it to the terminal or a file:

```sh
kscript openapi                          # JSON
kscript openapi --output yaml --out openapi.yaml
```

The document is OpenAPI 3.1. Each part comes from the code:

| In the document | From |
| --- | --- |
| paths and methods | the routes. `{id?}` and `{path*}` become `{id}` and `{path}`. A route for any method is listed under GET, POST, PUT, PATCH and DELETE. Redirects are left out. |
| path parameters | the controller's typed parameter (`id: int` is an integer), else the `where` rule (`int`, `number`, `uuid`, or a `pattern` for `slug`, `alpha` and regular expressions). |
| query parameters | `query: Search`: each field of the type is a query parameter, required unless it is optional. |
| request body | `json: NewPerson` or `body: NewPerson` as `application/json`, `form: T` as a form, `input: T` as either. |
| success answer | `/// @returns Person[]` (a type, then any words) for the schema. The status is 200, or the 2xx a `@response(..., status: 201)` gives. Views are `text/html`. |
| error answers | `@abort(404)` and `status: 409` in the controller. 422 for typed input that doesn't fit (the body lists the `problems`), 400 for a JSON body that isn't JSON, 404 for a path parameter with a type or rule, 401 for `auth`, 429 for `throttle`, 304 for `etag`. Errors share the `Error` schema. |
| summary, description | the controller's `///` doc comment (the first line is the summary, the rest the description), or one on the route itself. `@param id ...` describes a parameter. |
| deprecated | `@deprecated` in the doc comment. |
| security | the `auth` middleware, as a bearer token scheme. Every middleware is also listed under `x-middleware`. |
| schemas | each `type` and `enum` the routes use, with field descriptions and required fields, in `components.schemas`. |
| tags | the controller's import name (`people` for `people.show`). |
| info | `http.openapi.title` and `version`, else the project's `name` and `app.version`. |

Only the API routes (from `api.*` files) are included when there are any, otherwise every route. Set `http.openapi.routes` to `"all"` or `"api"` to choose. `"openapi": false` stops `kscript serve` answering `/_api`, `/_openapi.json` and `/_openapi.yaml`, for example on a public server. `kscript openapi` still works.

To keep the document in your project, for example to commit it or hand it to a client generator, set `http.openapi.file`:

```json
{ "http": { "openapi": { "file": "openapi.json" } } }
```

`kscript watch`, `kscript serve` and the editor (on save) then rewrite the file whenever the routes, controllers or types change. Name it `openapi.yaml` (or `.yml`) for YAML. The path is relative to kscript.json. `kscript watch --check` fails when the file is out of date, so CI can check it was committed.

## Editor and kscript check

Route files get the same checking and completion as scripts:

- `to` completes the functions of imported controllers (`@people.show`). A name that doesn't exist is an error with a did-you-mean.
- `middleware` and `use` complete the built-ins too, and accept `http.groups` names.
- Names in a list are checked and completed one by one, inside `[ ]` too.
- Route elements are only offered inside `Routes` or `Group`.
- `{id}` in `path` isn't read as a `{ }` expression.
- Doc comments on controllers show in hover, and also become the [API docs](#api-docs-openapi).

## How it runs

Each request gets a fresh interpreter. It runs the route files (and the scripts they import), matches the route, runs the middleware, then calls the controller. The route table is also kept between requests and rebuilt when a route file changes. That way, requests for static files and pages never run the route files. A request may take 5,000,000 evaluation steps, so an endless loop can't hang the server.
