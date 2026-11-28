# Routes, controllers and middleware: KScript.Http

## Introduction

KScript.Http turns `kscript serve` into a server framework. A route file maps HTTP methods and paths to controllers. A controller is an ordinary `fn` that returns a view, JSON or a response. Middleware runs around controllers, for logging, CORS, sign-in, rate limits or checks of your own.

Pages you already have keep working. `kscript serve` sends each request to the routes first, and anything no route matches goes on to the site's pages and files.

```sh
kscript serve samples/http-app      # serve the sample, then open http://localhost:8080/people
kscript routes samples/http-app     # print the route table
kscript openapi samples/http-app    # print the OpenAPI document made from the routes
```

[`samples/http-app`](../samples/http-app) is a complete site with web routes, API routes, a controller, views, custom middleware and a `kscript.json`.

## Basic routing

### Route files

A route file imports KScript.Http and holds a `Routes { }` block (`<KScript.Routes>` in XML), the same way a `Window { }` block makes a desktop app. Its imports go at the top of the file, above the block. The simplest route answers with a body of its own:

```kscript
// routes.kscript
import KScript.Http

Routes {
  Get(path: /ping) {
    return 'pong'
  }
}
```

`kscript serve` loads route files from these places in the site folder:

| File | Routes |
| --- | --- |
| `routes/web.kscript`, or `routes.kscript` at the top (or `.ks.xml`) | Web routes, mounted at `/`. |
| `routes/api.kscript`, or `api.kscript` at the top (or `.ks.xml`) | API routes, mounted under `/api`. Their errors and missing routes are answered with JSON. |

If you would like to load other files, list them in `http.routes` in `kscript.json` (see [Settings](#settings-kscriptjson)).

You may generate a route and its controller with `kscript make` (see [Generating files](projects.md#generating-files-kscript-make)):

```sh
kscript make route users                  # a route and its controller
kscript make route "people/{id}" --api    # an API route with a path parameter
kscript make route people --resource      # resource routes and a controller with their functions
```

`kscript make controller`, `request`, `middleware` and `view` make the rest.

### Methods

There is a route for each HTTP method: `Get`, `Post`, `Put`, `Patch`, `Delete` and `Options`. `Get` answers `HEAD` too. `Any` answers every method, and `Route(methods: [get, post])` answers the ones you list:

```kscript
// routes.kscript
import KScript.Http
import "controllers/people.kscript" as people

Routes {
  Get(path: /people, to: @people.index)
  Post(path: /people, to: @people.store)
  Route(path: /search, methods: [get, post], to: @people.index)
}
```

Routes are tried in the order they are written, and the first match wins. A path that matches but has no route for the method gets `405 Method Not Allowed` with an `Allow` header. An `OPTIONS` request gets `204` with `Allow`, which is how CORS preflights are answered (see [cors](#built-in-middleware)).

### How a route answers

Each route has a `path` and one way to answer it:

- `to:` names the controller, a function of an imported script (`@people.show`) or an `fn` in the route file itself (`@show`). In XML it is written `to="people.show"`.
- `view:` renders a [view](#views), with the path parameters as its variables.
- `redirect:` sends the browser somewhere else, with `302` unless you give a `status:`.
- A `{ }` block is the route's own body, like an `fn`.

```kscript
// routes.kscript
import KScript.Http
import "controllers/people.kscript" as people

Routes {
  Get(path: /, view: "home")
  Get(path: /people/{id}, to: @people.page)
  Get(path: /team, redirect: "/people", status: 301)
}
```

The route file runs on every request, before the route is matched. So it may use `if` to add routes only in some cases, and its imports are new for each request, just like a page's.

## Route parameters

A part of the path in braces is a route parameter. Its value reaches the controller as the variable of the same name, so `{id}` becomes `$id`:

```kscript
// routes.kscript
import KScript.Http
import "controllers/people.kscript" as people

Routes {
  Get(path: /people/{id}, to: @people.page)
  Get(path: /archive/{year?}, to: @people.index)
  Get(path: /files/{path*}, to: @people.index)
}
```

`{year?}` is optional, and `{path*}` takes the rest of the path. A path is a [slash path](types.md#slash-paths), so it needs no quotes: `path: /people/{id}` and `path: "/people/{id}"` are the same. Either way, `{id}` is a route parameter and is never filled in from a variable.

### Rules for parameters

`where:` gives a parameter a rule. A request that breaks the rule doesn't match the route, so the next route gets a try:

```kscript
// routes.kscript
import KScript.Http
import "controllers/people.kscript" as people

Routes {
  Get(path: /people/{id}, to: @people.page, where: { id: int })
  Get(path: /posts/{slug}, to: @people.index, where: { slug: slug })
}
```

The rules are `number`, `int`, `alpha`, `alnum`, `slug`, `uuid`, or a regular expression in quotes. In XML the same rules are written `where="id: int, slug: slug"`.

## Named routes

`name:` gives a route a name, so you can make its address with `@route()` instead of writing the path out. Names must be unique:

```kscript
// routes.kscript
import KScript.Http
import "controllers/people.kscript" as people

Routes {
  Get(path: /people/{id}, to: @people.page, name: "people.page")
}
```

`@route('people.page', id: 3)` then gives `/people/3`. A controller can read the name of the route that matched it from `$request.route`.

## Route groups

`Group` shares a path prefix, middleware or a name prefix between the routes inside it. Groups nest:

```kscript
// routes.kscript
import KScript.Http
import "controllers/people.kscript" as people

Routes {
  Group(prefix: /admin, middleware: auth, name: "admin.") {
    Get(path: /people, to: @people.index, name: "people")
  }
}
```

That route answers `/admin/people`, only for someone signed in, and is named `admin.people`.

`Middleware(use: [log, cors])` adds middleware for the routes after it, in the same file or group. More on that under [Middleware](#middleware).

## Controllers

### Writing a controller

A controller is a plain `fn`, usually in a script of its own that the route file imports. It returns what the response should be:

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
}
```

### What a controller is given

A controller's parameters are filled in by name. Ask only for what it needs:

| Parameter | What it holds |
| --- | --- |
| a path parameter, such as `id` | The value from the path: `{id}` becomes `$id`. |
| `request` | The [page request fields](web.md), including `scheme` and `url`. A matched route also gives `ip`, `json`, `input` and `route` (its name, or `null`). See [The request](#the-request). |
| `json` | The body read as JSON, or `null` if it wasn't JSON. |
| `body` | The JSON if there was any, otherwise the raw text. |
| `input` | The query, form and JSON fields together. |
| `query`, `form`, `headers`, `cookies` | Each part of the request on its own. |

### Typed input

A typed parameter is converted for you, so `id: int` gives a number. Numbers aren't rounded to fit: `1.9` for an `int`, or a number too large to hold, answers `422` (or `404` in the path), while `2` and `2.0` are both 2.

A `type` maps the request's data into a record, so the controller only ever sees data of the right shape:

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

Data that doesn't fit is the client's mistake, so the controller doesn't run. A body, query or form that doesn't fit gets `422 Unprocessable Entity`, and API routes answer with JSON that lists every problem:

```json
{ "error": "...", "status": 422, "problems": [{ "path": "$.age", "field": "age", "message": "..." }] }
```

A path parameter that doesn't fit, such as `/people/abc` for `id: int`, gets `404`.

### Route model binding

A path parameter typed as a [KScript.Data](data.md) model is the record with that key, so the controller gets the post itself. A key with no record, or one that isn't a key, gets `404`, and the API docs show the key's type:

```kscript
// routes: Get(path: /posts/{post}, to: @posts.show)
fn show(post: Post) => $post
```

Anything the route runs may ask for that record with `@inject()`, so it doesn't have to be handed down. A function the controller calls names the model:

```kscript
fn word_count() => @inject(Post).body->split(' ')->count()
```

A [component](components.md#take-the-routes-record-with-inject) takes it as an input's default, so `<post-card />` in the view needs no `post="..."`:

````kscript
// _components/post-card.kscript
import KScript.Web
import "../models/post.kscript"

type PostCard extends Component {
  construct({ post: Post = @inject() })

  fn render() => ```html
    <h2>{$post.title}</h2>
    ```
}
````

`@inject()` looks for the path parameter with the input's name (`{post}` for `post`), then for the route's one record of that model. If you would like to name the parameter yourself, you may write `@inject(from: article)`.

A route answered with `view:` has only the key, so the record is found when it is first asked for, and a key with no record gets `404` there too. A route with no record of the model, such as `/posts/{id}` with `id: int`, is an error that tells you to pass one: `Post->find($id)`.

### Authorizing a route

A route may ask the model's [policy](auth.md#policies) before its controller runs. `can: update` asks about the one record that the controller's typed path parameters name, and answers `403` when the policy says no:

```kscript
// routes/api.kscript
import KScript.Http
import KScript.Auth
import "../models/post.kscript"
import "../controllers/posts.kscript" as posts

Routes {
  Put(path: /posts/{post}, to: @posts.update, can: update)
}
```

A guest the ability turns away is sent to sign in, or gets `401` on an API route. A route that holds two records can't tell which one is meant, so its controller calls `@authorize(update, $post)` for the one it means. A route answered with `view:` has no controller to type the record, so its components ask with `@inject(can: read)`.

### Returning a response

What a controller returns becomes the response:

| Return | Response |
| --- | --- |
| A map, list, number or true/false | JSON (`application/json`). |
| Text | HTML. |
| `@view('people/show', person: $p)` | The view, rendered with its layout. `status:` sets the status: `@view('auth/login', data: { error: $err.message }, status: 429)`. |
| `@response(body, status: 201, type: 'text/csv', headers: { … })` | Your own status, type and headers. A map or list body is JSON. |
| `@redirect(/people)`, `@redirect(to: '/new', status: 301)` | A redirect. |
| nothing (`null`) | `204 No Content`. |

When you need a status or headers of your own, return `@response()`:

```kscript
// controllers/people.kscript
kscript {
  import KScript.Http

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
    person = { id: 3, name: $json.name }
    return @response($person, status: 201, headers: { Location: @route('people.show', id: $person.id) })
  }
}
```

A `base64` body, such as `@response(@read_bytes('logo.png'), type: 'image/png')`, sends the original bytes. Its content type is `application/octet-stream` unless you give one.

To stop a request from anywhere, call `@abort()`: `@abort(404)` or `@abort(403, message: 'Admins only')`. It works in a controller, a function it calls, or middleware, and `try` doesn't catch it.

Controllers, and actions in views rendered with `@view()`, may read and write files within the site folder. Set `"write_files": false` to make both read-only. Anything a controller prints with `echo` goes to the server log.

### Errors and status codes

An [error type](language.md#error-types) can carry a status. When a controller, or anything it calls, throws one and nothing catches it, the request is answered with that status and the error's message, without the file and line. API routes answer with JSON that has the error's type and fields as well:

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

These error types are built in, so `throw Forbidden "Admins only"` works without a declaration, and an error type that extends one answers with its status:

| Error type | Status |
| --- | --- |
| `BadRequest` | 400 |
| `Unauthorized` | 401 |
| `Forbidden` | 403 |
| `NotFound` | 404 |
| `Conflict` | 409 |
| `Validation` | 422 |
| `TooManyRequests` | 429, with `Retry-After` when the error has a `retry_after` field |

Give an error type a status of its own with `error PaymentFailed(status: 402)`. Unlike `@abort()`, these are ordinary errors, so a `try` in the controller can still catch them. An error without a status answers `500`, with the file and line.

While you work on a site, a `500` shows the error's message, file and line, as JSON on API routes, and the server log shows it too. In production (`kscript serve --env production`, or `KSCRIPT_ENV=production`) the answer is only `Something went wrong on our side. Please try again later.`, and the details go to the server's log. Errors with a status of their own, and `@abort()`, say the same in both.

A request whose body can't be read answers `413` when it is over a size or field limit, and `400` when it is cut short or isn't what its `Content-Type` says, as JSON on API paths.

## Views

`@view('people/show', person: $p)` renders `views/people/show.html` (or `.md`). If that file doesn't exist, it renders `people/show.html` in the site folder. You may leave off the extension. The named values become the view's variables, and `$request` is there too. Views are ordinary pages, so layouts, components, `<include>` and `<live>` all work in them.

The `views` folder is never served as pages, because a view needs its controller's data.

The editor reads every `@view('people/show', ...)` in the project's scripts, so it knows what each view is given. In `views/people/show.html`, typing `$` offers `$person`, `$person.` offers the fields the controller gave it, and a loop over a list it was given knows the fields of each item. Hovering `$person` says which controller passes it, and **Go to Definition** opens that call. A view that names a package with `<import package="KScript.Auth" />` gets that package's variables too, so `$session->` lists the session's methods.

## Middleware

### Writing middleware

Middleware is a function that takes the request and a `next` function. Return a response to stop the request, or call `$next($request)` to carry on, and change the response it gives back if you like:

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

The two parameters are positional. They may be untyped, and more parameters may follow if they have defaults. KScript checks the signature when it loads the route, and the editor checks every reference to the function.

### Using middleware

Middleware applies at four levels, and the outermost runs first:

1. `http.middleware` in `kscript.json`, for every route.
2. `Routes(middleware: …)`, for every route in the file. `Middleware(use: …)` adds more for the routes after it.
3. `Group(middleware: …)`, for the routes inside the group.
4. `Get(…, middleware: …)`, for one route.

```kscript
// routes.kscript
import KScript.Http
import "middleware/security.kscript"

Routes {
  Middleware(use: [secure_headers, json_only])
}
```

A middleware list is an array of names, such as `[log, secure_headers, throttle:30]`. Each name is a built-in, a function (one from an imported script too), or a group name from `http.groups`. A built-in takes its argument after a colon, as in `throttle:30`. A single name needs no brackets: `middleware: auth`.

In XML, attributes are text, so the same list is written `use="secure_headers, json_only"`. In `.kscript` files, a list written as one string (`use: "log, cors"`) is an error (KS6006), and `kscript check --fix` rewrites it as an array. The same goes for every list of names: `only`, `except`, `methods` and `<llm tools>`.

### Built-in middleware

| Name | What it does |
| --- | --- |
| `log` | Writes each request to the server log: what answered it, the route name, the status and the time. |
| `cors` | Answers browser preflight requests and adds `Access-Control-Allow-Origin`. The allowed origins, methods, headers and credentials come from `http.cors`. |
| `auth` | Lets through someone signed in with [KScript.Auth](auth.md), by the session cookie or an API token, and sets `$request.user`. It also accepts `Authorization: Bearer <token>` matching the `API_TOKEN` environment variable (`http.auth.token_env` names another). Anyone else is sent to `http.auth.login` (`/login`) in a browser, or gets `401` when they want JSON. `auth:session` and `auth:token` accept only one kind of sign-in. `auth:ADMIN_TOKEN` accepts only the token in that variable, and refuses every request (`500`) when it isn't set. Requests that pass have `$request.authenticated` set. |
| `guest` | Lets through only people who aren't signed in, for the sign-in and sign-up pages. Others are sent to `http.auth.home` (`/`), or get `403` when they want JSON. |
| `csrf` | Needs the session's CSRF token on every request except `GET`, `HEAD` and `OPTIONS`, as a `_token` form field or an `X-CSRF-Token` header. Without it, `419 Page Expired`. Web routes in a file that imports KScript.Auth get it on their own (see [Forms and CSRF](auth.md#forms-and-csrf)). |
| `throttle` | Allows 60 requests a minute per client address and route (`http.throttle`), or the number you give: `throttle:30`. Over the limit gets `429` with `Retry-After`. Every response has `X-RateLimit-Limit` and `X-RateLimit-Remaining`. |
| `etag` | Tags `GET` answers with an ETag, and answers `If-None-Match` with `304 Not Modified` (see [Caching and ETags](#caching-and-etags)). `etag:weak` gives weak tags. |
| `cache_control` | Sets `Cache-Control` on `GET` answers: `cache_control:300` (public for five minutes) or `cache_control:private;max-age=60`. |
| `cache_response` | Keeps whole `GET` answers in memory and answers again from there: `cache_response:5m` (one minute if you don't say). |

A function with the same name as a built-in wins, so you may replace one.

## Caching and ETags

Browsers and proxies can keep an answer and ask again only when it may have changed. KScript.Http helps at three levels, with [KScript.Cache](cache.md) underneath.

### ETags

`etag: true` on a route, or the `etag` middleware, adds an `ETag` header to every `200` answer to `GET` and `HEAD`: a hash of the body. When the browser asks again with `If-None-Match` and the tag still matches, it gets `304 Not Modified` with no body, so nothing is sent twice:

```kscript
import KScript.Http
import "controllers/people.kscript" as people

Routes {
  Get(path: /people, to: @people.index, etag: true, cache: 60)
  Get(path: /people/{id}, to: @people.show, etag: true, cache: "private, max-age=0, must-revalidate")
}
```

The route still runs before its body is hashed. To skip the work as well, a controller may name its version itself with `@etag(value)`. The value can be anything that changes when the answer would, such as the record or its `updated_at`. When the browser already has that version, the request stops right there with `304`:

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

`@etag(value, weak: true)` makes a weak tag. You may also tag a response by hand with `->with_etag()` (a hash of its body) or `->with_etag($value)`.

To tag every route at once, put `etag` in `http.middleware`, or in a group:

```jsonc
{ "http": { "middleware": ["log", "etag"] } }
```

### Cache-Control

`cache:` on a route, or the `cache_control` middleware, sets `Cache-Control` on successful `GET` answers and on `304`s. A number alone is a public max-age in seconds, so `cache: 300` gives `public, max-age=300`. Otherwise, list the directives: `public`, `private`, `no-cache`, `no-store`, `no-transform`, `must-revalidate`, `proxy-revalidate`, `must-understand`, `immutable`, `max-age=`, `s-maxage=`, `stale-while-revalidate=` and `stale-if-error=`. A directive the browser wouldn't understand is an error when the route file loads. A header the controller sets itself, with `->with_cache('no-store')`, wins.

### Answers kept on the server

`cache_response:5m` keeps whole `200` answers to `GET` in KScript.Cache's memory store for that long, by URL, and answers again from memory without running the controller. Answers carry `X-Cache: HIT` or `MISS`.

So that one person's answer is never given to another, a request with an `Authorization` header or any cookie (a session, a login) always runs the controller and is never kept. Neither is an answer that sets a cookie, starts a session (a form's CSRF token does) or is marked `private` or `no-store`. That makes `cache_response` right for public pages and API answers read without cookies, while a signed-in visitor's page is always made fresh.

## Resources

`Resource` adds the usual JSON routes for a controller in one line. `Resource(path: /people, to: people)` adds these routes for the controller imported as `people`:

| Method | Path | Function | Name |
| --- | --- | --- | --- |
| GET | /people | `index` | people.index |
| POST | /people | `store` | people.store |
| GET | /people/{id} | `show` | people.show |
| PUT, PATCH | /people/{id} | `update` | people.update |
| DELETE | /people/{id} | `destroy` | people.destroy |

A function the controller doesn't have gets no route. `only: [index, show]` and `except: [destroy]` pick the actions, `name:` changes the name prefix, and `param:` changes `{id}`. For example, you may put the write actions behind `auth`:

```kscript
// routes/api.kscript
import KScript.Http
import "../controllers/people.kscript" as people

Routes {
  Resource(path: /people, to: people, only: [index, show])
  Group(middleware: auth) {
    Resource(path: /people, to: people, only: [store, update, destroy])
  }
}
```

With `model:`, each action asks the model's [policy](auth.md#policies) before the controller runs:

```kscript
// routes/api.kscript
import KScript.Http
import KScript.Auth
import "../models/post.kscript"
import "../controllers/posts.kscript" as posts

Routes {
  Resource(path: /posts, to: posts, model: Post)
}
```

| Action | Ability asked | About |
| --- | --- | --- |
| `index` | None | A list has no single record. |
| `store` | `create` | The model. |
| `show` | `read` | The record `{id}` names. |
| `update` | `update` | The same record. |
| `destroy` | `delete` | The same record. |

A key with no record gets `404` before the policy is asked.

## The request

`$request`, or a controller's `request` parameter, is a map with these keys:

| Key | What it holds |
| --- | --- |
| `method` | The method, in upper case. |
| `path`, `url` | The path, and the path with its query. |
| `query`, `form`, `json`, `input` | The query, the form fields, the JSON body, and all three together. |
| `body` | The raw text of the body. |
| `headers` | The headers, with lower-case names. |
| `cookies` | The cookies, by name. |
| `params` | The path parameters. |
| `ip` | The client's address. |
| `route` | The matched route's name, or `null`. |

A value in `query` or `form` is text. A name sent more than once (`?tag=a&tag=b`) is a list of texts, so no value is lost. A typed field says which you want: `tags: string[]` takes the name sent once or many times and is always a list, while `tag: string` answers `422` when it is sent twice.

## Responses

`@response()`, `@view()` and `@redirect()` give a response value, and middleware gets one back from `$next($request)`. You may read it and change it:

- Read it with `.status`, `.body`, `.type`, `.headers`, `->header(name)`, `.is_ok` and `->json()`.
- Change it with `->with_status(code)`, `->with_header(name, value)`, `->with_type(type)`, `->with_etag(value)` and `->with_cache(directives)`. Each returns the response, so the calls chain.
- `->with_header('Set-Cookie', ...)` adds a cookie each time it is called, rather than replacing the last, and each cookie is sent as a `Set-Cookie` header of its own.
- `@response($answer)` given a response returns it as it is, with its status, and `@response($answer, status: 410)` changes the status.

`kscript serve` sends `Server: kscript` on HTTP responses. To send your own value for a route, use `->with_header('Server', 'my-app')`.

## Calling your own API

`@api()` from [KScript.Net](language.md), given a path instead of an address, asks the project's own routes, in the same process and without the network:

```kscript
import KScript.Net

notes = @api(/api/notes)->check()->json()
added = @api(/api/notes, post: { title: 'Ship it' })
echo $added.status   // 201
```

It works from pages, tests and scripts in the project (the folder with `kscript.json`), and from routes. The arguments and the response are the same as for an address: `post:` and the other methods, `options: { headers: … }`, then `.status`, `->json()`, `->header(name)` and the rest. A path with no route is a `404`.

The method and its body are one named argument, and the options people set most have one too:

| Argument | What it does |
| --- | --- |
| `post: { title: 'Ship it' }`, `put: $note`, `patch: $changes` | Sends that method with this body. A map or list is sent as JSON. Without one, the request is a GET. |
| `delete: true` | Sends a DELETE with no body. Anything else is sent as the body. |
| `bearer: $token` | Adds `Authorization: Bearer …` to the headers. |
| `timeout: 5s`, `retries: 3` | The same as those fields in `options`. |
| `as: Note` | The response must succeed, and its JSON becomes `Note` records. |

`post: null` and `delete: false` send nothing, so a helper may pass its own arguments straight on: `@api($url, post: $post, delete: $delete)`. An option given both ways is an error, so a request says each thing once.

`@api()` sends `User-Agent: kscript` by default, to remote and local addresses alike. To name your own client, pass `options: { headers: { 'User-Agent': 'my-app' } }`, which applies to that one request.

### Cookies and sign-in

A request made while answering another one, such as a page calling the API or a route calling another route, sends on the `Cookie` and `Authorization` headers of the request being answered, so the API sees the same signed-in person. Give your own `headers` to send others. A `Cookie` header you give is read into the route's `$cookies`, as `kscript serve` reads it. Cookies a response sets are never carried over to a later request:

```kscript
import KScript.Net

// The route reads $cookies.sb_guest as '0f8f'.
reply = @api(/board, options: { headers: { Cookie: 'sb_guest=0f8f' } })
// The cookies the response set, such as { ks_session: '...', sb_guest: '...' }.
echo $reply.cookies
// A list when the response set more than one.
echo $reply.headers['Set-Cookie']
```

A header sent more than once, such as `Set-Cookie`, is a list in `.headers`, and `->header(name)` gives the first. `.cookies` has the name and value of each cookie the response set. Requests inside one another stop after 8, so a route that calls itself is an error rather than a hang.

The Notes example in the [fullstack project template](projects.md#a-web-app-with-its-api---template-fullstack) works this way: its pages get their notes from `/api/notes`, so the pages and other clients share one API.

## Settings: kscript.json

The `http` section of `kscript.json` holds the settings for routes and their middleware. Every one is optional:

```jsonc
{
  "http": {
    "routes": ["routes.kscript", { "file": "api/v1.kscript", "prefix": "/api/v1", "api": true }],  // default: routes/web.*, routes/api.*, routes.* and api.*
    "middleware": ["log"],                      // for every route
    "groups": { "api": ["cors", "throttle"] },  // names for lists of middleware
    "views": "views",                           // where @view() looks first; never served
    "api_prefix": "/api",                       // the prefix of api.* files
    "write_files": true,                        // false: controllers and their live actions can only read files
    "cors": { "origins": ["*"], "methods": ["GET", "POST"], "headers": ["Content-Type", "Authorization"], "credentials": false },
    "auth": { "token_env": "API_TOKEN", "login": "/login", "lifetime": "2h" },  // and the KScript.Auth settings in auth.md
    "throttle": { "limit": 60, "seconds": 60 },
    "openapi": { "routes": "auto", "title": "People API", "version": "1.2.0", "file": "openapi.json" }  // or false
  }
}
```

Every setting is also in the [kscript.json reference](/reference/settings).

## API docs: OpenAPI

The routes already say everything an API document needs, so KScript writes one for you. While `kscript serve` runs, open `/_api` for a page that lists every endpoint with its parameters, body and answers, with a form to try each one. Tools such as Postman, client generators and gateways read the same document at `/_openapi.json` or `/_openapi.yaml`. `kscript openapi` writes it to the terminal or a file:

```sh
kscript openapi                                    # print the document as JSON
kscript openapi --output yaml --out openapi.yaml   # write it as YAML to openapi.yaml
```

The document is OpenAPI 3.1, and each part comes from your code:

| In the document | From |
| --- | --- |
| paths and methods | The routes. `{id?}` and `{path*}` become `{id}` and `{path}`. A route for any method is listed under GET, POST, PUT, PATCH and DELETE. Redirects are left out. |
| path parameters | The controller's typed parameter (`id: int` is an integer), else the `where` rule (`int`, `number`, `uuid`, or a `pattern` for `slug`, `alpha` and regular expressions). |
| query parameters | `query: Search`: each field of the type is a query parameter, required unless it is optional. |
| request body | `json: NewPerson` or `body: NewPerson` as `application/json`, `form: T` as a form, `input: T` as either. |
| success answer | `/// @returns Person[]` (a type, then any words) for the schema. The status is 200, or the 2xx that a `@response(..., status: 201)` gives. Views are `text/html`. Without `@returns`, an API route's answer is JSON of any shape, and a page route's has no declared body. |
| error answers | `@abort(404)` and `status: 409` in the controller. 422 for typed input that doesn't fit (the body lists the `problems`), 400 for a JSON body that isn't JSON, 404 for a path parameter with a type or rule, 401 for `auth`, 429 for `throttle`, 304 for `etag`. API routes' errors share the `Error` schema, and a page route's errors are `text/html`. |
| summary, description | The controller's `///` doc comment (the first line is the summary, the rest the description), or one on the route itself. `@param id ...` describes a parameter. |
| deprecated | `@deprecated` in the doc comment. |
| security | The `auth` middleware, as a bearer token scheme. Every middleware is also listed under `x-middleware`. |
| schemas | Each `type` and `enum` the routes use, with field descriptions and required fields, in `components.schemas`. Fields a type doesn't name are left out of the record without an error, so a schema says `additionalProperties: false` only for `type T(extra: error)` (which answers `422`), and `true` for `extra: keep`. |
| tags | The controller's import name (`people` for `people.show`). |
| info | `http.openapi.title` and `version`, else the project's `name` and `app.version`. |

Only the API routes (from `api.*` files) are included when there are any, otherwise every route. Set `http.openapi.routes` to `"all"` or `"api"` to choose. `"openapi": false` stops `kscript serve` answering `/_api`, `/_openapi.json` and `/_openapi.yaml`, for example on a public server, while `kscript openapi` still works.

To keep the document in your project, for example to commit it or hand it to a client generator, set `http.openapi.file`:

```json
{ "http": { "openapi": { "file": "openapi.json" } } }
```

`kscript watch`, `kscript serve` and the editor (on save) then rewrite the file whenever the routes, controllers or types change. Name it `openapi.yaml` (or `.yml`) for YAML. The path is relative to `kscript.json`. `kscript watch --check` fails when the file is out of date, so CI can check it was committed.

## In the editor

Route files get the same checking and completion as scripts:

- `to:` completes the functions of imported controllers (`@people.show`). A name that doesn't exist is an error with a did-you-mean.
- `middleware:` and `use:` complete the built-ins too, and accept `http.groups` names.
- Names in a list are checked and completed one by one, inside `[ ]` too.
- Route elements are only offered inside `Routes` or `Group`.
- `{id}` in a `path` isn't read as a `{ }` expression.
- Doc comments on controllers show in hover, and also become the [API docs](#api-docs-openapi).

## How it runs

Each request gets a fresh interpreter. It runs the route files and the scripts they import, matches the route, runs the middleware, then calls the controller. The route table is also kept between requests and rebuilt when a route file changes, so requests for static files and pages never run the route files. A request may take 5,000,000 evaluation steps, so an endless loop can't hang the server.
