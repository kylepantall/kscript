# Auth App

A `kscript serve` site with accounts, made with `kscript make project auth-app --bare --web --api` and then `kscript make auth --api`. See the [Sign-in guide](../../docs/auth.md).

```sh
kscript serve
```

Open http://localhost:8080/register to make an account. You are signed in straight away, and `/account` shows your details and lets you make API tokens. Password reset links from `/forgot-password` are written to the server's log, because there is no mail provider yet.

To try passkeys, open `/account` at `http://localhost:8080` after signing in. Choose **Add a passkey** and follow your device's prompt. Sign out, then choose **Sign in with a passkey** at `/login`. Direct sign-in returns to the home page. If you opened a protected page such as `/profile`, either sign-in method returns you there. To revoke a passkey, choose **Remove passkey** beside its registration date on the account page. For deployment, set `http.auth.passkey_origin` in `kscript.json` to the site's public HTTPS origin.

To try a model-bound component, run `kscript db migrate` from this folder and open `/profile` while signed in. Enter a bio and choose **Save profile**, or turn on **Save as I type** to save after a half-second pause. **Show saved bio** reads the persisted value and keeps your current draft in the editor. `_components/profile-editor.kscript` receives a typed `record` prop, reads it as `$.record` inside `render()`, and uses the `edit_profile` binding in `routes/web.kscript`. It permits the account to update its own bio. Profiles live in `_data/profile.sqlite`.

With the API:

```sh
curl -X POST localhost:8080/api/tokens -H "Content-Type: application/json" \
  -d '{ "email": "you@example.com", "password": "your password" }'
curl localhost:8080/api/me -H "Authorization: Bearer ks_..."
```

Users and sessions are kept in the app's own data and state folders, not in this folder.
