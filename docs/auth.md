# Sign-in: KScript.Auth

KScript.Auth gives a `kscript serve` app accounts that people sign in to. It keeps the users, hashes their passwords with argon2id, and keeps their sessions in a cookie with safe defaults. It also checks a CSRF token on every form, limits failed sign-ins, and handles password resets and API tokens. The `email` and `password` types check what people type, so the rules live in one place: your kscript.json.

The quickest start is `kscript make auth`, which writes the pages and routes for you:

```
kscript make auth            # sign up, sign in, sign out, forgot password, account page
kscript make auth --api      # and POST /api/tokens + GET /api/me for API tokens
```

Then `kscript serve`, open `/register` to make an account, and `/account` to see it. The rest of this guide explains what those files do, so you can change them or write your own.

## What kscript make auth writes

| File | What it is |
| --- | --- |
| `routes/web.kscript` (added to) | The sign-in routes: a `guest` group for the forms and an `auth` group for the account page. |
| `routes/api.kscript` (with `--api`, added to) | `POST /api/tokens` and `GET /api/me`. |
| `controllers/accounts.kscript` | The controller: sign up, sign in, sign out, resets, the account page and tokens. |
| `requests/sign-up.kscript` | The `SignUp` type the sign-up form is checked against, imported by the controller. |
| `views/auth/*.html` | The pages: login, register, forgot-password, reset-password and account. |
| `_layout.html` | A plain layout, only when the site has none. |
| `kscript.json` (added to) | `"types": { "password": { "min": 10 } }`, the rule new passwords must meet. |

It respects `make.syntax` and `--syntax xml`, the folders in `paths`, and the route files `http.routes` lists. It won't replace files or routes you already have unless you add `--force`, and `--dry-run` shows what it would do.

## Signing up and in

A controller imports KScript.Auth. `@register` adds a user, `@attempt` checks an email and password and signs them in, and `@logout` signs them out.

```kscript
// requests/sign-up.kscript
type SignUp {
  name: string
  email: email
  password: password
}
```

```kscript
// controllers/accounts.kscript
import KScript.Http
import KScript.Auth
import "../requests/sign-up.kscript"

fn sign_up(form: map) {
  data = SignUp($form)
  user = @register($data.email, $data.password, name: $data.name)
  @login($user)
  return @redirect(@intended())
}

fn sign_in(form: map) {
  user = @attempt($form.email, $form.password, remember: $form.remember == 'yes')
  if $user == null {
    return @view('auth/login', data: { error: "That email and password don't match an account." })->with_status(422)
  }
  return @redirect(@intended())
}

fn sign_out() {
  @logout()
  return @redirect('/')
}
```

- `@register(email, password, fields...)` checks the email and password with their types and your project's rules. It refuses an email someone already signed up with (case doesn't matter), keeps only an argon2id hash of the password, and keeps the other named values as the user's fields. A problem is a `Validation` error that names the field, which a route answers with 422.
- `@attempt(email, password, remember?, session?)` returns the user, or `null`. When the email has no account it takes about as long, so the timing doesn't give away who has one. A hash made with older settings is made again. `session: false` only checks, for an API that answers with a token.
- `@login(user, remember?)` signs a user in. The session gets a new id, so an id seen before the sign-in can't be used. `remember: true` keeps them signed in for `http.auth.remember` (30 days) after the browser closes.
- `@logout(everywhere?)` ends the session and removes its cookie. `everywhere: true` ends the user's sessions on every device.
- `@user()` is the signed-in user (a map with `id`, `email`, `created_at` and their fields, never the hash), or `null`. `@is_signed_in()` is true when there is one.
- `@intended(default?)` is the page the `auth` middleware sent someone away from, or the default (`http.auth.home`, `/`). It is always a path on this site, so it can't send people elsewhere.

### Failed sign-ins

After `http.auth.attempts` (5) failures for the same email and address within `http.auth.lockout` (a minute), `@attempt` throws a `TooManyRequests` error that says how long to wait. A route answers it with 429 and a `Retry-After` header. Catch it to show the form again:

```kscript
fn sign_in(form: map) {
  user = null
  try {
    user = @attempt($form.email, $form.password)
  } catch err when $err.type == 'TooManyRequests' {
    return @view('auth/login', data: { error: $err.message })->with_status(429)
  }
  return @redirect(@intended())
}
```

## Protecting routes

The `auth` middleware lets signed-in users through and sets `$request.user`. A browser is sent to `http.auth.login` (`/login`) and comes back afterwards. A request that wants JSON (an API route, an `Authorization` header, or `Accept: application/json`) gets a 401 instead. The `guest` middleware is the other way round: it sends people who are signed in to `http.auth.home`.

```kscript
import KScript.Http
import KScript.Auth
import "controllers/accounts.kscript" as accounts

Routes {
  Group(middleware: guest) {
    Get(path: "/login", to: @accounts.show_login, name: "login")
    Post(path: "/login", to: @accounts.sign_in)
  }
  Group(middleware: auth) {
    Get(path: "/account", to: @accounts.account)
    Post(path: "/logout", to: @accounts.sign_out)
  }
}
```

| Middleware | Lets through |
| --- | --- |
| `auth` | A user signed in by the session or an API token, or the shared `API_TOKEN` (see [Built-in middleware](http.md#built-in-middleware)). |
| `auth:session` | Only a user signed in by the session. |
| `auth:token` | Only a user's API token. It always answers 401, never a redirect. |
| `auth:ADMIN_TOKEN` | Only the bearer token in that environment variable, as before KScript.Auth. |
| `guest` | Only people who aren't signed in. Others are redirected home, or get a 403 when they want JSON. |
| `csrf` | Only requests with the session's CSRF token. Web routes get it on their own (see below). |

## Forms and CSRF

A web route whose file imports KScript.Auth checks a CSRF token on every POST, PUT, PATCH and DELETE. That stops another site from submitting a form as someone who is signed in. Put the token in each form with `{! @csrf_field() }`:

```html
<import package="KScript.Auth" />
<form method="post" action="/login">
  {! @csrf_field() }
  <input type="email" name="email">
  <input type="password" name="password">
  <button>Sign in</button>
</form>
```

Scripts in the page send `@csrf_token()` as an `X-CSRF-Token` header instead. A request without the right token gets `419 Page Expired`. API routes aren't checked, since they use tokens rather than cookies, and neither is a request with a bearer token and no cookie. Set `http.auth.csrf` to `false` to turn the check off.

## The session

`$session` keeps values between requests for the same browser. It is kept on the server, and the browser only holds a random id in a cookie.

```kscript
import KScript.Auth

$session->set('theme', 'dark')
theme = $session->get('theme', 'light')
$session->flash('status', 'Your profile was saved.')
```

| Method | What it does |
| --- | --- |
| `->get(key, default?)` | The value, or the default. |
| `->has(key)` | True when the key is there. |
| `->set(key, value)` | Keeps a value. Values are kept as JSON, and a password is refused. |
| `->flash(key, value)` | Keeps a value for the next request only: a message to show after a redirect. |
| `->remove(key)` | Removes a key. True if it was there. |
| `->all()` | Every value, as a map. |
| `->regenerate()` | Gives the session a new id with the same values. `@login` does this for you. |
| `.token` | The CSRF token, like `@csrf_token()`. |

Keys starting with `_` are kept by KScript.Auth, so they can't be set. The cookie is `HttpOnly` and `SameSite=Lax`. It is `Secure` on https, behind a proxy that sends `X-Forwarded-Proto: https`, or with `SameSite=None`. A session lasts `http.auth.lifetime` (2 hours) after the last request.

## Users

`@users()` is the app's users. They are kept as JSON files in the app's data folder (see [App folders](storage.md)), or in `http.auth.folder`, or in a model (see [Users in a model](#users-in-a-model)).

| Method | What it does |
| --- | --- |
| `->find(id)` | The user with this id, or `null`. |
| `->find_email(email)` | The user with this email (case doesn't matter), or `null`. |
| `->all()` | Every user, oldest first. |
| `->count()` | How many users there are. |
| `->verify(email, password)` | The user with this email and password, or `null`. Unlike `@attempt`, it doesn't sign them in or count failures. |
| `->update(id, fields...)` | Changes a user's fields (`null` removes one). A new email is checked like `@register`'s. |
| `->set_password(id, password)` | Gives a user a new password, checked by the password type. |
| `->delete(id)` | Deletes a user with their sessions, API tokens and reset tokens. |

Users from a database of your own work too: `@login({ id: $row.id, email: $row.email })` signs in any map with an `id`, and the session keeps a copy of it.

### Users in a model

To keep the users in the app's database, name a [KScript.Data](data.md) model in `http.auth.users`:

```json
{ "http": { "auth": { "users": "User" } } }
```

The model is the one in the models folder (`models/user.kscript`). It needs an email field and a password field, and any other fields you give `@register` or `->update`:

```kscript
import KScript.Data

model User(timestamps, unique: [email], hidden: [password]) {
  email: email
  password: password
  name: string
  posts: Post[]
}
```

Then a user is a row of the `users` table, and `@user()`, `@register` and `@users()` give you the model's record, so its relations and methods work: `@user().posts`. The password field holds the hash KScript.Auth makes; `hidden: [password]` keeps it out of JSON. Ids are the model's keys. Sessions, API tokens and reset tokens are still files in the app's state folder.

## Password resets

`@reset_token(email)` starts a reset and returns a token for a link, or `null` when no user has that email. Tell the person the same thing either way, so the page doesn't show who has an account. The token works once, for `http.auth.reset_lifetime` (an hour), and a new one replaces the last. `@reset_password(token, password)` checks the new password, sets it, and ends the user's sessions. It returns the user, or `null` when the token is wrong, used or too old.

```kscript
import KScript.Http
import KScript.Auth

fn send_reset(form: map, request: map) {
  token = @reset_token($form.email)
  if $token != null {
    link = 'https://' + $request.headers.host + @route('password.reset', token: $token)
    log "Password reset link for {$form.email}: {$link}"
  }
  $session->flash('status', 'If that email has an account, a link is on its way.')
  return @redirect('/forgot-password')
}
```

The controller `kscript make auth` writes logs the link. Swap the `log` line for your mail provider to email it.

## API tokens

`@api_token(user, name?, expires?)` makes a token for a user. Show it once, because only its hash is kept. A request that sends `Authorization: Bearer <token>` is signed in as that user, so `@user()` and the `auth` middleware work as they do with a session. `expires` is a time such as `'30d'`. Without it, a token lasts until it is revoked.

```kscript
import KScript.Http
import KScript.Auth

fn issue_token(json: map) {
  user = @attempt($json.email, $json.password, session: false)
  if $user == null {
    throw Unauthorized "That email and password don't match an account."
  }
  return @response({ token: @api_token($user, name: 'cli', expires: 30d) }, status: 201)
}
```

`@api_tokens(user)` lists a user's tokens without the tokens themselves: `[{ id, name, created_at, last_used_at, expires_at }]`. `@revoke_token(token or id, user?)` revokes one. With `user`, it only revokes a token that belongs to that user, so an account page can let people revoke their own.

## Settings: kscript.json

Every setting has a safe default, so set only what you want to change. Times are seconds, or text such as `"15m"`, `"2h"` or `"30d"`.

```json
{
  "types": { "password": { "min": 12 } },
  "http": {
    "auth": {
      "login": "/login",
      "home": "/",
      "lifetime": "2h",
      "remember": "30d",
      "attempts": 5,
      "lockout": "1m",
      "reset_lifetime": "1h"
    }
  }
}
```

| Setting | Default | What it does |
| --- | --- | --- |
| `login` | `/login` | Where `auth` sends people who aren't signed in. |
| `home` | `/` | Where `guest` sends people who are, and `@intended()`'s default. |
| `sessions` | `file` | `file` keeps sessions in the app's state folder, so they last across restarts. `memory` keeps them until kscript stops. |
| `folder` | the app folders | A folder, relative to the site, for users, sessions and tokens. |
| `users` | files | A KScript.Data model in the models folder, such as `User`, that holds the users. See [Users in a model](#users-in-a-model). |
| `lifetime` | `2h` | How long a session lasts after the last request. |
| `remember` | `30d` | How long a "remember me" sign-in lasts. |
| `cookie` | `ks_session` | The session cookie's name. |
| `secure` | https only | `true` or `false` forces the cookie's `Secure` flag. |
| `same_site` | `Lax` | The cookie's `SameSite`: `Lax`, `Strict` or `None`. |
| `csrf` | `true` | `false` turns off the CSRF check. |
| `attempts` | `5` | Failed sign-ins allowed per email and address. |
| `lockout` | `1m` | How long failures are counted over, and how long `@attempt` then waits. |
| `reset_lifetime` | `1h` | How long a reset token works. |
| `token_env` | `API_TOKEN` | The environment variable with the shared token the `auth` middleware also accepts. |

The password rules are the `password` type's, under `types` (see [Types and validation](types.md)). `@register`, `@reset_password` and `->set_password` use them, and so do typed fields such as `password: password` in a `type`.
