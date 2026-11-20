# Auth App

A `kscript serve` site with accounts, made with `kscript make project auth-app --bare --web --api` and then `kscript make auth --api`. See the [Sign-in guide](../../docs/auth.md).

```sh
kscript serve
```

Open http://localhost:8080/register to make an account. You are signed in straight away, and `/account` shows your details and lets you make API tokens. Password reset links from `/forgot-password` are written to the server's log, because there is no mail provider yet.

With the API:

```sh
curl -X POST localhost:8080/api/tokens -H "Content-Type: application/json" \
  -d '{ "email": "you@example.com", "password": "your password" }'
curl localhost:8080/api/me -H "Authorization: Bearer ks_..."
```

Users and sessions are kept in the app's own data and state folders, not in this folder.
