# Request ID

Install the HTTP middleware used in KScript's API template.

```sh
kscript lib add request-id
```

Import the middleware in `routes/api.kscript`.

```kscript
import KScript.Http
import "../lib/request-id/index.kscript"
```

Add `request_id` to your routes' middleware list, for example `Routes(middleware: [cors, request_id])`. The response carries the caller's `X-Request-Id` header when supplied. For a request without that header, the middleware generates a UUID.
