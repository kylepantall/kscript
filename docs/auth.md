# Sign-in: KScript.Auth

## Introduction

KScript.Auth gives a `kscript serve` app accounts that people sign in to. It keeps the users, hashes their passwords with argon2id, and keeps their sessions in a cookie with safe defaults. It also checks a CSRF token on every form, limits failed sign-ins, and handles password resets and API tokens. The `email` and `password` types check what people type, so the rules live in one place: your kscript.json.

### Quick start

The quickest start is `kscript make auth`, which writes the pages, routes and controller for you:

```sh
kscript make auth              # sign up, sign in, sign out, forgot password and an account page
kscript make auth --api        # the same, plus POST /api/tokens and GET /api/me for API tokens
kscript make auth --passkeys   # accounts that sign in with a passkey instead of a password
```

Then run `kscript serve`, open `/register` to make an account, and open `/account` to see it. The rest of this guide explains what those files do, so you may change them or write your own.

## What kscript make auth writes

`kscript make auth` writes or adds to these files:

| File | What it is |
| --- | --- |
| `routes/web.kscript` (added to) | The sign-in routes: a `guest` group for the forms and an `auth` group for the account page. |
| `routes/api.kscript` (with `--api`, added to) | `POST /api/tokens` and `GET /api/me`. |
| `controllers/accounts.kscript` | The controller: sign up, sign in, sign out, resets, the account page and tokens. |
| `requests/sign-up.kscript` | The `SignUp` type the sign-up form is checked against, imported by the controller. |
| `_components/*.kscript` | The [components](components.md) the pages are made of: the sign-in, sign-up and password forms, the account details, the passkey list and the API tokens. |
| `views/auth/*.html` | The pages: login, register, forgot-password, reset-password and account. Each holds a heading and its components. |
| `_layout.html` | A plain layout, only when the site has none. |
| `models/user.kscript` | The `User` model the users are kept in: a copy of KScript.Auth's, to change as your app needs (see [Users in a model](#users-in-a-model)). With `--passkeys` its `email` and `password` are optional, because someone who signs up with a passkey has neither. |
| `kscript.json` (added to) | `"types": { "password": { "min": 10 } }`, the rule new passwords must meet, and `"users": "User"` in `http.auth`. |

It respects `make.syntax` and `--syntax xml`, the folders in `paths` (components go in `paths.components`), and the route files `http.routes` lists. It won't replace files or routes you already have unless you add `--force`, and `--dry-run` shows what it would do.

## Pages made of components

Each page `kscript make auth` writes is a heading and a few components, so a form can be moved, restyled or used on another page without copying it. The controller passes what a form needs to show again, and the page hands it on:

```html
<!-- views/auth/login.html -->
<layout path="/_layout.html" title="Sign in">
  <h1>Sign in</h1>
  <sign-in-form email="{$data.email ?? ''}" error="{$data.error}" />
  <p><a href="/forgot-password">Forgot your password?</a> · <a href="/register">Create an account</a></p>
</layout>
```

The component lists its inputs in `construct`, so `kscript check` reports a page that passes anything else:

````kscript
// _components/sign-in-form.kscript
import KScript.Web
import KScript.Auth

type SignInForm extends Component {
  construct({ email: string = '', error?: string })

  fn render() => ```html
    <status-message />
    <p class="error" @if($error != null)>{$error}</p>
    <form method="post" action="/login">
      {! @csrf_field() }
      <p><label>Email<br><input type="email" name="email" value="{$email}" required autocomplete="email"></label></p>
      <p><label>Password<br><input type="password" name="password" required autocomplete="current-password"></label></p>
      <p><button>Sign in</button></p>
    </form>
    ```
}
````

| Component | What it shows |
| --- | --- |
| `<sign-in-form>` | The email and password form, and **Sign in with a passkey** when the site has passkeys. |
| `<sign-up-form>` | The name, email and password form, with the problems the `SignUp` type found. |
| `<forgot-password-form>`, `<reset-password-form>` | Asking for a reset link, and choosing the new password. |
| `<status-message>` | The message a controller flashed before its redirect. |
| `<account-details>` | Who is signed in, and **Sign out**. |
| `<passkey-list>` | The account's passkeys, to add or remove one. |
| `<api-tokens>` | The account's API tokens, to make or revoke one. |

Signing in and out changes the session cookie, so those forms post to the controller as usual. Making and revoking tokens and passkeys doesn't, so the account page's components do it themselves in [live actions](components.md#actions-and-browser-events), and `/account` is a plain view route: `Get(path: /account, view: 'auth/account')`. Their actions are `private`, so nothing outside the component can call them:

````kscript
// _components/api-tokens.kscript
import KScript.Web
import KScript.Auth

type ApiTokens extends Component {
  name: string
  private shown?: string

  private fn make() {
    shown = @api_token(@user(), name: $name->trim())
    name = ''
  }

  private fn revoke(id: string) {
    shown = null
    @revoke_token($id, user: @user())
  }

  fn render() => ```html
    <h2>API tokens</h2>
    <if test="$shown != null"><p class="status">Your new token is <code>{$shown}</code>. Copy it now: it won't be shown again.</p></if>
    <ul>
      <for each="t" in="@api_tokens(@user())">
        <li>
          {$t.name}, made {$t.created_at}
          <button type="button" @on-click(call: $revoke, with: [$t.id], confirm: "Revoke {$t.name}?")>Revoke</button>
        </li>
      </for>
    </ul>
    <form @on-submit($make)>
      <p><label>Name<br><input @bind($name, on: action) required></label></p>
      <p><button live-disable>Make a token</button></p>
    </form>
    ```
}
````

A live action runs the page's route and its `auth` middleware again, so `@user()` is the signed-in user and a guest can't use it. Each action carries a proof signed with the session's CSRF token, so no other site can send one.

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
    return @view('auth/login', data: { error: "That email and password don't match an account." }, status: 422)
  }
  return @redirect(@intended())
}

fn sign_out() {
  @logout()
  return @redirect(/)
}
```

- `@register(email, password, fields...)` checks the email and password with their types and your project's rules. It refuses an email someone already signed up with (case doesn't matter), keeps only an argon2id hash of the password, and keeps the other named values as the user's fields. A problem is a `Validation` error that names the field, which a route answers with 422.
- `@attempt(email, password, remember?, session?)` returns the user, or `null`. When the email has no account it takes about as long, so the timing doesn't give away who has one. A hash made with older settings is made again, with the algorithm the project's [password type](types.md#project-defaults-in-kscriptjson) names (argon2id unless it says `bcrypt`). `session: false` only checks, for an API that answers with a token.
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
    return @view('auth/login', data: { error: $err.message }, status: 429)
  }
  return @redirect(@intended())
}
```

## Passkeys

A passkey signs someone in with their device's lock (a fingerprint, a face or a PIN) instead of a password. KScript supplies the browser code and the HTTP endpoints, so you only add buttons.

### Accounts without passwords

To create accounts using passkeys from the first visit, run this in your project:

```sh
kscript make auth --passkeys   # passkey sign-up, sign-in and an account page
```

Open `/register` and choose **Create account with a passkey**. KScript creates the account, a `User` record with no email or password, and starts its session after the device verifies the ceremony. Records the account owns can extend [Ownable](#guests-and-owners) or hold a `user: User` relation. Every passkey added on `/account` signs in to that same user. Add a second passkey before removing the first. An account with a single passkey must keep it.

The generator sets `http.auth.passkey_signup` and uses the configured serve port for localhost. For a deployed app, generate with `--origin https://accounts.example.com`. Add `--rp-id example.com` if you want that parent domain to scope the credentials. Add `--api` for the authenticated identity endpoint. A signed-in user can issue tokens from the account page. This kit has no password registration, sign-in or reset routes.

### Adding passkeys to password accounts

In a project made with `kscript make auth`, set the public origin in `kscript.json` and restart `kscript serve`. For the default local server, use the following setting.

```json
{ "http": { "auth": { "passkey_origin": "http://localhost:8080" } } }
```

Sign in to an existing account, open `/account` and choose **Add a passkey**. Sign out, then choose **Sign in with a passkey** on `/login`. The browser asks you to unlock your passkey. Each successful sign-in starts a fresh KScript session, so existing `auth:session` routes and [model forms](web.md#model-forms) work with it. You can enroll multiple passkeys and remove a lost one from the account page.

### Passkey buttons on your own pages

The `<sign-in-form>`, `<sign-up-form>` and `<passkey-list>` components hold these buttons. For your own pages, put them on the sign-in and signed-in account pages respectively. KScript supplies the browser code and the HTTP endpoints.

```html
<button type="button" passkey="signup" passkey-redirect="/account">Create account with a passkey</button>
<button type="button" passkey="login" passkey-redirect="/account">Sign in with a passkey</button>
<button type="button" passkey="register">Add a passkey</button>
```

Use `@has_passkeys()` from KScript.Auth to show the controls when the site has enabled them. After sign-in the browser returns to the protected URL that prompted it, or to `http.auth.home`. The optional `passkey-redirect` overrides that destination with a page on the same origin. From a signed-in account page, `@passkeys()` lists that user's credential IDs and creation dates. Call `@revoke_passkey($id)` from a live action, as `<passkey-list>` does, or from a CSRF-protected POST to remove one.

### Your own browser code

For a custom browser UI, POST `{}` as `application/json` to `/_ks/passkeys/signup/options` for a new account, `/_ks/passkeys/register/options` to add a credential, or `/_ks/passkeys/login/options` to sign in. Use the returned public-key options with the browser's credentials API. POST the resulting credential JSON to `verify` in the same path, from the same origin and session. Verification returns `{"ok":true}` on success and includes the destination in `redirect` for sign-in. Each challenge expires after five minutes and can be verified once, on the process that issued it. Restarting a server discards pending ceremonies. When using several workers, route both steps to the same worker. Accounts and completed credentials persist in the auth store.

### Deploying passkeys

For deployment, set `passkey_origin` to the exact public HTTPS origin, including a non-default port. The relying party ID defaults to its hostname. To keep credentials scoped to a parent domain you control, set `passkey_rp_id` before enrolling users. Changing the relying party ID requires enrolling new credentials. Back up the `_passkeys` folder alongside your auth user data. Use `@users()->delete(id)` to remove a stored account and its passkeys. If a separate system manages your users, give each account an immutable ID that is never reused.

## Protecting routes

To let only signed-in people reach a route, put it behind the `auth` middleware. The `auth` middleware lets signed-in users through and sets `$request.user`. A browser is sent to `http.auth.login` (`/login`) and comes back afterwards. A request that wants JSON (an API route, an `Authorization` header, or `Accept: application/json`) gets a 401 instead. The `guest` middleware is the other way round: it sends people who are signed in to `http.auth.home`.

```kscript
import KScript.Http
import KScript.Auth
import "controllers/accounts.kscript" as accounts

Routes {
  Group(middleware: guest) {
    Get(path: /login, to: @accounts.show_login, name: "login")
    Post(path: /login, to: @accounts.sign_in)
  }
  Group(middleware: auth) {
    Get(path: /account, view: 'auth/account')
    Post(path: /logout, to: @accounts.sign_out)
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

## Policies

A policy says who may do what to a model's records. It is one block per model, and each function in it is an ability: it takes the user, then the record, and returns `true` or `false`.

```sh
kscript make policy Post    # write policies/post.kscript
```

```kscript
// policies/post.kscript
import KScript.Auth
import "../models/post.kscript"
import "../models/user.kscript"

/// Who may do what to a Post.
policy for Post {
  /// Staff, with an address at example.com, may write a new post.
  fn create(user: User) => $user.email->ends_with('@example.com')

  /// Anyone may read a published post. Its author may read a draft.
  fn read(user: User?, post: Post) => $post.published or $user?.id == $post.author_id

  /// Only the author may change or delete a post.
  fn update(user: User, post: Post) => $user.id == $post.author_id
  fn delete(user: User, post: Post) => $user.id == $post.author_id
}
```

The four usual abilities are `create`, `read`, `update` and `delete`. `create` has no record yet, so it takes the user only. A policy can hold any other ability too, such as `fn publish(user: User, post: Post)`. Policies live in the `policies` folder (or `paths.policies` in `kscript.json`), one file per model, and are found there without an import.

| The user parameter | A guest |
| --- | --- |
| `user: User` | Is turned away before the ability runs, and asked to sign in. |
| `user: User?` | Reaches the ability with `null`, so the ability decides. |

The user is `@user()`: the user type your project keeps (see [Users in a model](#users-in-a-model)), or a map. Write `user: map?` when you have no user type.

### Deciding before every ability

A function called `before` runs first. `true` allows and `false` refuses, whatever the ability says. `null` carries on to the ability.

```kscript
// policies/post.kscript
import KScript.Auth
import "../models/post.kscript"
import "../models/user.kscript"

policy for Post {
  fn before(user: User) => $user.email == 'admin@example.com' ? true : null

  fn update(user: User, post: Post) => $user.id == $post.author_id
}
```

### Asking a policy

| Where | Write | When the answer is no |
| --- | --- | --- |
| A page or view | `@can(update, $post)`, `@cannot(update, $post)` | They return `false` and `true`, so the page shows or hides something. |
| A controller or function | `@authorize(update, $post)` | The request stops with `403`. |
| A route | `can: update` | The controller doesn't run, and the answer is `403`. |
| A resource | `model: Post` | Each action asks its own ability. |
| A component or type | `@inject(can: read)` | The request stops with `403`. |

For an ability with no record, name the model: `@can(create, Post)`.

A page shows a link only to someone who may use it:

```html
<!-- views/posts/show.html -->
<import package="KScript.Auth" />
<import path="../../models/post.kscript" />
<h1>{@inject(Post, can: read).title}</h1>
<if test="@can(update, @inject(Post))">
  <a href="/posts/{@inject(Post).id}/edit">Edit</a>
</if>
```

A controller stops with `@authorize()`, which returns the record it was given:

```kscript
// controllers/posts.kscript
import KScript.Http
import KScript.Auth
import "../models/post.kscript"

fn edit(post: Post) {
  @authorize(update, $post)
  return @view('posts/edit', { post: $post })
}

fn store(json: map) {
  @authorize(create, Post)
  return Post->create(title: $json.title, author_id: @user().id)
}
```

A route asks before its controller runs. `can:` asks about the one record the controller's typed path parameters name:

```kscript
// routes/api.kscript
import KScript.Http
import KScript.Auth
import "../models/post.kscript"
import "../controllers/posts.kscript" as posts

Routes {
  Put(path: /posts/{post}, to: @posts.update, can: update)
  Delete(path: /posts/{post}, to: @posts.destroy, can: delete)
}
```

A resource with `model:` asks for each of its actions: `show` asks `read`, `store` asks `create`, `update` asks `update` and `destroy` asks `delete`. `index` isn't asked, because a list has no single record.

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

A component that takes the route's record with [`@inject()`](components.md#take-the-routes-record-with-inject) asks as it takes it:

````kscript
// _components/post-card.kscript
import KScript.Web
import "../models/post.kscript"

type PostCard extends Component {
  construct({ post: Post = @inject(can: read) })

  fn render() => ```html
    <h2>{$post.title}</h2>
    ```
}
````

### What a refused user gets

| Who | Answer |
| --- | --- |
| A signed-in user the policy refuses | `403`, with "You are not allowed to update this Post." |
| A guest, on an ability that needs a user, in a browser | A redirect to `http.auth.login`, and back afterwards. |
| The same guest on an API route, or asking for JSON | `401`, with "Sign in first." |

In a script, `@authorize()` throws `Forbidden` or `Unauthorized`, which a `try` can catch.

### Testing a policy

`@acting_as()` sets who is asking until the test ends, in the test and in the routes it calls with `@api(/...)`. Pass `null` for a guest.

```kscript
// tests/post-policy.test.kscript
import KScript.Auth
import KScript.Data
import "../models/post.kscript"
import "../models/user.kscript"

test(name: "only the author may update a post") {
  post = Post->create(title: 'Mine', author_id: 1)

  @acting_as(User(id: 1))
  expect(@can(update, $post), equals: true)

  @acting_as(User(id: 2))
  expect(@can(update, $post), equals: false)
}
```

### In the editor

`kscript check` and the editor read the policies folder. An ability the policy doesn't have, `@can(publsh, $post)`, is an error that lists the ones it has. An `@inject()` that takes a record of a model with a policy and asks nothing is a warning ([KS5017](errors.md#ks5017)): write `@inject(can: read)`, or check with `@can()` where the record is used.

## Forms and CSRF

A web route whose file imports KScript.Auth checks a CSRF token on every POST, PUT, PATCH and DELETE. That stops another site from submitting a form as someone who is signed in. Put the token in each form with `{! @csrf_field() }`, in a page or a component:

````kscript
// _components/sign-out-button.kscript
import KScript.Web
import KScript.Auth

type SignOutButton extends Component {
  fn render() => ```html
    <form method="post" action="/logout">
      {! @csrf_field() }
      <button>Sign out</button>
    </form>
    ```
}
````

Scripts in the page send `@csrf_token()` as an `X-CSRF-Token` header instead. A live component's actions need no field: each one carries a proof signed with the session's CSRF token. A request without the right token gets `419 Page Expired`. API routes aren't checked, since they use tokens rather than cookies, and neither is a request with a bearer token and no cookie. Set `http.auth.csrf` to `false` to turn the check off.

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

Keys starting with `_` are kept by KScript.Auth, so they can't be set. The cookie is `HttpOnly` and `SameSite=Lax`. It is `Secure` on https, behind a proxy that sends `X-Forwarded-Proto: https`, or with `SameSite=None`. A session lasts `http.auth.lifetime` (2 hours) after the last request. Sessions and reset tokens that have run out are removed from the state folder now and then, as new ones are written.

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

To keep the users in the app's database, name a [KScript.Data](data.md) model in `http.auth.users`. `kscript make auth` does this for you:

```json
{ "http": { "auth": { "users": "User" } } }
```

KScript.Auth comes with a `User` model: a name, an email and a password, with timestamps, in a `users` table it makes the first time it's used (`auto_create`). `kscript make auth` (or `kscript publish User`) copies it to `models/user.kscript`, where it is yours to change: add fields, relations or a key of your own. Any model in the models folder works too. It needs an email field and a password field, and any other fields you give `@register` or `->update`:

```kscript
import KScript.Data

model User(unique: [email], hidden: [password]) extends Timestamps {
  name: string
  email: email
  password: password
  posts: Post[]
}
```

When you move to [migrations](data.md#migrations), take `auto_create` off and let `kscript db diff` write the table.

Then a user is a row of the `users` table, and `@user()`, `@register` and `@users()` give you the model's record, so its relations and methods work: `@user().posts`. The password field holds the hash KScript.Auth makes; `hidden: [password]` keeps it out of JSON. Ids are the model's keys. Sessions, API tokens and reset tokens are still files in the app's state folder.

With `passkey_signup` on, someone who signs up with a passkey becomes a record with no email or password, so the model's `email` and `password` must be optional (`email?: email`, `password?: password`). `kscript make auth --passkeys` writes them that way. Their passkeys are kept with the auth data, in the `_passkeys` folder.

## Guests and owners

Some apps let people use them before they sign up: a board of notes, a basket, a draft. KScript.Auth gives every browser a guest id, and the `Ownable` mixin keeps each row to whoever made it, guest or user.

```kscript
import KScript.Data
import KScript.Auth
import KScript.Http

/// A note on someone's board.
model Note extends [Ownable, Timestamps] {
  title: string
}

Routes {
  Get(path: /notes) { return Note->mine()->order_by(id)->get() }
  Post(path: /notes) { return Note->create(title: $json.title) }
  Get(path: /notes/{id}) { return Note->mine()->find_or_fail($id) }
}
```

`Ownable` comes with KScript.Auth, so there is nothing to declare: importing the package is enough. Its owner is a [relation](data.md#relations), so it is typed: `$note.user` is a `User` record, and `->with(user)` and `->where(user: $ada)` work as they do for any relation. It adds:

| Member | What it does |
| --- | --- |
| `user?: User` | The user the row belongs to, or `null` when a guest made it. It is stored as `user_id`, a foreign key to `users`, so deleting a user deletes their rows. |
| `guest?: Guest` | The guest the row belongs to, or `null` when a signed-in user made it. It is stored as `guest_id`. |
| `->mine(as?:)` | A scope: the rows of whoever is making the request. `as:` names a user instead. |
| `->is_mine()` | Whether this record belongs to whoever is making the request. |

Exactly one of `user` and `guest` is set. `Note->create()` fills it in itself, so a request can't make a row for someone else: giving another `user:` or a `guest:` is a `Forbidden` error. Neither can be changed once the row is made (`->update(user:)` is refused). Outside a request (a seeder, a job, `kscript run`), nobody is making the request, so name the owner: `Note->create(title: 'Welcome', user: $ada)` and `Note->mine(as: $ada)`. Without one, both are `Unauthorized` errors.

Ownable's user is a record of the model the users are kept in, so an app with Ownable models keeps its users in a model: `"users": "User"` in `http.auth`, which `kscript make auth` sets for you (see [Users in a model](#users-in-a-model)). Without it, making a row as a signed-in user is a `Config` error that says so.

- `@guest()` is this browser's guest id: the key of its `Guest`, a `uuid` unless your copy of `Guest` says otherwise. The first call makes one and sends it in the `ks_guest` cookie (`http.auth.guest_cookie`), which lasts `http.auth.guest_lifetime` (a year). It is `null` when someone is signed in, and outside a request. A guest is stored in the `guests` table the first time it owns a row.

### Changing User, Guest and Ownable

`User`, `Guest` and `Ownable` are KScript models that come with KScript.Auth. To change one, copy it into your project with `kscript publish`, then edit the copy. The copy replaces KScript.Auth's model everywhere: in scripts, routes, tests, migrations and the editor.

```sh
kscript publish          # list the models the packages ship
kscript publish User     # copy User to models/user.kscript
kscript publish Guest Ownable
```

The relations follow the models. Give your `User` a uuid key and Ownable's `user` is stored as a uuid too:

```kscript
import KScript.Data

model User(key: 'uid', auto_create, unique: [email], hidden: [password]) extends Timestamps {
  uid: uuid = @uuid()
  name: string
  email: email
  password: password
}
```

Guests work the same way. `Guest`'s key is `id: uuid = @uuid()`, made in the browser's cookie and stored when the guest first owns something. Leave the key out of your copy, and guests are numbered by the database instead: a new guest is stored at once, to get its number. Fields you add to `Guest` are there for each guest, such as a display name.

### Keeping a guest's rows when they sign up

With `"adopt_guest": true` in `http.auth`, signing in (with `@login`, `@attempt` or a passkey) moves the guest's rows to their account. It happens once per model, the first time each model is used after sign-in, and includes soft-deleted rows. The guest cookie is cleared. With it off (the default), a guest's rows stay theirs, and the account starts empty.

### Testing as a guest

`@acting_as_guest(id?:)` makes the rest of the test run as a guest, in the test and in the routes it calls with `@api(/...)`. It returns the guest's id: a new one each call, or the one you give it. `@acting_as(...)` switches back to a user.

```kscript
import KScript.Auth
import KScript.Net

test(name: 'guests see only their own notes') {
  @acting_as_guest()
  @api('/api/notes', post: { title: 'milk' })
  @acting_as_guest()
  expect(@api('/api/notes')->json(), equals: [])
}
```

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
  return @redirect(/forgot-password)
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
| `passkey_origin` | disabled | The public HTTPS origin that can register and use passkeys. Local development accepts `http://localhost` with an optional port. |
| `passkey_signup` | `false` | Allow a guest to create a passwordless account by verifying a new passkey. Requires `passkey_origin`. |
| `passkey_rp_id` | origin hostname | The relying party domain for credentials. |
| `guest_lifetime` | `365d` | How long a browser keeps its [`@guest()`](#guests-and-owners) id. |
| `guest_cookie` | `ks_guest` | The guest id cookie's name. |
| `adopt_guest` | `false` | `true` moves a guest's rows of [Ownable](#guests-and-owners) models to their account when they sign in. |
| `token_env` | `API_TOKEN` | The environment variable with the shared token the `auth` middleware also accepts. |

The password rules are the `password` type's, under `types` (see [Types and validation](types.md)). `@register`, `@reset_password` and `->set_password` use them, and so do typed fields such as `password: password` in a `type`.
