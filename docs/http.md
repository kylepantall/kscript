# Routes, controllers and middleware: KScript.Http

KScript.Http gives `kscript serve` a server framework in the spirit of Laravel's `routes/web.php` and `routes/api.php`. Route files map HTTP methods and paths to controllers. Controllers are ordinary `fn` functions that return a view, JSON or a response. Middleware runs around them for logging, CORS, auth, rate limits or your own checks.

The page features you already have keep working. `kscript serve` sends each request to the routes first. Anything no route matches goes on to the site's pages and files.

```sh
kscript serve samples/http-app      # then open http://localhost:8080/people
kscript routes samples/http-app     # print the route table
```

[`samples/http-app`](../samples/http-app) is a complete site: web routes in XML, API routes in the `.kscript` format, a controller, views, custom middleware and a `kscript.json`.

## Route files

A file made of a `KScript.Routes { }` block (`<KScript.Routes>` in XML) is a route file, the same way `KScript.Window` makes a desktop app. The block imports KScript.Http for you. `kscript serve` loads these files from the site folder:

| File | Routes |
| --- | --- |
| `routes/web.kscript`, or `routes.kscript` at the top (or `.ks.xml`) | Web routes, mounted at `/`. |
| `routes/api.kscript`, or `api.kscript` at the top (or `.ks.xml`) | API routes, mounted under `/api`. Errors and missing routes are answered with JSON. |

`http.routes` in `kscript.json` can list other files instead (see [Settings](#settings-kscriptjson)). `kscript make route users` adds a route and makes its controller (see [Generating files](projects.md#generating-files-kscript-make)): `kscript make route "people/{id}" --api`, `kscript make route people --resource`, and `kscript make controller`, `request`, `middleware` and `view` for the rest.

```kscript
// routes.kscript
KScript.Routes {
  import "controllers/people.kscript" as people
  import "middleware/security.kscript"

  Middleware(use: secure_headers)

  Get(path: /, view: home, name: home)
  Get(path: /people, to: people.list, name: people.list)
  Get(path: /people/{id}, to: people.page, name: people.page, where: id: int)
  Get(path: /team, redirect: /people, status: 301)

  Get(path: /ping) {
    return 'pong'
  }
}
```

| Element | Use |
| --- | --- |
| `Get` `Post` `Put` `Patch` `Delete` `Options` `Any` | A route for that method. `Get` answers `HEAD` too. |
| `Route(methods: "get, post")` | A route for several methods. |
| `Resource(path: /people, to: people)` | The usual JSON resource routes, answered by the controller's `index`, `store`, `show`, `update` and `destroy` functions (see [Resources](#resources)). |
| `Group(prefix, middleware, name)` | Routes that share a path prefix, middleware or a name prefix. Groups nest. |
| `Middleware(use: "log, cors")` | Middleware for the routes after it in this file or group. |

A route has a `path` and one way to answer:

| Attribute | Use |
| --- | --- |
| `path` | `/people/{id}`. `{name?}` is optional and `{name*}` takes the rest of the path (`/files/{path*}`). |
| `to` | The controller: a function of an imported script (`people.show`) or an `fn` in the route file (`show`). |
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
- `request`: the whole request.
- `json`: the body read as JSON (`null` if it wasn't JSON).
- `body`: the JSON if there was any, otherwise the raw text.
- `input`: the query, form and JSON fields together.
- `query`, `form`, `headers`, `cookies`.

A typed parameter is converted, so `id: int` gives a number. A `type` maps the request data into a record, so the controller only sees data of the right shape:

```kscript
type NewPerson(fields: "name: string, age?: int, role?: string")

fn store(json: NewPerson) {
  // $json is a NewPerson record
  // …
}
```

Data that doesn't fit is the client's mistake, so the controller doesn't run. A body, query or form that doesn't fit gets `422 Unprocessable Entity`. API routes answer with JSON that lists every problem: `{ "error": "...", "status": 422, "problems": [{ "path": "$.age", "field": "age", "message": "..." }] }`. A path parameter that doesn't fit, such as `/people/abc` for `id: int`, gets `404`.

```kscript
// controllers/people.kscript
kscript {
  import KScript.IO
  import KScript.Http

  fn all() => @read_json('data/people.json')
  fn find(id) => @all()->find(fn($p) => $p.id == $id)

  fn index(query) {
    // GET /api/people?role=Founder
    return @all() |> filter(fn($p) => $query.role == null or $p.role == $query.role)
  }

  fn page(id) {
    // GET /people/2
    person = @find($id) ?? @abort(404, message: 'Nobody has that id.')
    return @view('people/show', person: $person)
  }

  type NewPerson(fields: "name: string, age?: int, role?: string")

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
| A map, list, number or yes/no | JSON (`application/json`). |
| Text | HTML. |
| `@view('people/show', person: $p)` | The view rendered with its layout. |
| `@response(body, status: 201, type: 'text/csv', headers: { … })` | Your own status, type and headers. A map or list body is JSON. |
| `@redirect('/people')`, `@redirect(to: '/new', status: 301)` | A redirect. |
| nothing (`null`) | `204 No Content`. |

`@abort(404)` or `@abort(403, message: 'Admins only')` stops the request with that status, from anywhere: a controller, a function it calls, or middleware. `try` doesn't catch it. An error in a controller answers `500` with the file and line, and the server log shows it too.

Controllers can read and write files with KScript.IO, but only inside the site folder. Set `"write_files": false` to make them read-only like pages. Anything a controller prints with `echo` goes to the server log.

## Views

`@view('people/show', person: $p)` renders `views/people/show.html` (or `.md`). If that file doesn't exist, it renders `people/show.html` in the site folder. Leaving off the extension is fine. The named values become the view's variables, and `$request` is there too. Views are normal pages, so layouts, components, `<include>` and `<live>` all work in them.

The `views` folder is never served as pages, because a view needs its controller's data. The editor knows that views get their variables from a controller, so it doesn't flag them as undefined.

## Middleware

Middleware is a function that takes the request and `$next`. It can stop the request by returning a response, or call `$next($request)` and change the response it gets back:

```kscript
// middleware/security.kscript
kscript {
  import KScript.Http

  fn secure_headers(request, next) {
    return $next($request)->with_header('X-Frame-Options', 'DENY')
  }

  fn json_only(request, next) {
    if $request.method == 'POST' and not ($request.headers['content-type'] ?? '')->contains('json') {
      return @response({ error: 'Send JSON.' }, status: 415)
    }
    return $next($request)
  }
}
```

Middleware applies at four levels, and it runs outermost first:

1. `http.middleware` in `kscript.json`, for every route.
2. `KScript.Routes(middleware: …)`, for every route in the file. `Middleware(use: …)` adds more for the routes after it.
3. `Group(middleware: …)`, for the routes inside the group.
4. `Get(…, middleware: …)`, for one route.

A middleware list is comma separated. Each entry can be a built-in, a function, or a group name from `http.groups`. Built-ins take an argument after a colon, such as `throttle:30`.

### Built-in middleware

| Name | What it does |
| --- | --- |
| `log` | Writes each request to the server log: what answered it, the route name, the status and the time. |
| `cors` | Answers browser preflight requests and adds `Access-Control-Allow-Origin`. The allowed origins, methods, headers and credentials come from `http.cors`. |
| `auth` | Needs `Authorization: Bearer <token>` to match the `API_TOKEN` environment variable. Set `http.auth.token_env` to use another variable, or name one per route with `auth:ADMIN_TOKEN`. If the variable isn't set, every request is refused (500), never let through. Requests that pass have `$request.authenticated` set. |
| `throttle` | Allows 60 requests a minute per client address and route (`http.throttle`), or `throttle:30`. Over the limit gets `429` with `Retry-After`. Every response has `X-RateLimit-Limit` and `X-RateLimit-Remaining`. |

A function with the same name as a built-in wins, so you can replace one.

## Resources

`Resource(path: /people, to: people)` adds these routes for the controller imported as `people`:

| Method | Path | Function | Name |
| --- | --- | --- | --- |
| GET | /people | `index` | people.index |
| POST | /people | `store` | people.store |
| GET | /people/{id} | `show` | people.show |
| PUT, PATCH | /people/{id} | `update` | people.update |
| DELETE | /people/{id} | `destroy` | people.destroy |

Functions the controller doesn't have get no route. `only="index, show"` and `except="destroy"` pick actions. `name` changes the name prefix, and `param` changes `{id}`. Put the write actions in an auth group:

```
Resource(path: "/people", to: people, only: "index, show")
Group(middleware: auth) {
  Resource(path: "/people", to: people, only: "store, update, destroy")
}
```

## The request

`$request` (or a controller's `request` parameter) is a map with these keys:

- `method` (upper case), `path`, `url` (path and query), `query`, `form`, `json`, `input`, `body` (the raw text).
- `headers`, with lower-case names, and `cookies`.
- `params` (the path parameters), `ip` and `route` (the route's name).

## Responses

`@response()`, `@view()` and `@redirect()` give a response value. Middleware gets one back from `$next($request)`.

- Read it with `->status()`, `->body()`, `->type()`, `->headers()`, `->header(name)`, `->ok()` and `->json()`.
- Change it with `->with_status(code)`, `->with_header(name, value)` and `->with_type(type)`. Each returns the response, so the calls chain.

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
    "auth": { "token_env": "API_TOKEN" },
    "throttle": { "limit": 60, "seconds": 60 }
  }
}
```

## Editor and kscript check

Route files get the same checking and completion as scripts:

- `to` completes the functions of imported controllers (`people.show`). A name that doesn't exist is an error with a did-you-mean.
- `middleware` and `use` complete the built-ins too, and accept `http.groups` names.
- Route elements are only offered inside `KScript.Routes` or `Group`.
- `{id}` in `path` isn't read as a `{ }` expression.

## How it runs

Each request gets a fresh interpreter. It runs the route files (and the scripts they import), matches the route, runs the middleware, then calls the controller. The route table is also kept between requests and rebuilt when a route file changes. That way, requests for static files and pages never run the route files. A request may take 5,000,000 evaluation steps, so an endless loop can't hang the server.
