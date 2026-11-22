# Queues and jobs: KScript.Queue

A job is a piece of work you hand off to run later, away from the script or request that asked for it: sending an email, resizing a picture, calling a slow API. `->dispatch()` puts a job on a queue and returns at once. `kscript worker` takes jobs off the queue and runs them, tries again when one fails, and keeps the ones that keep failing so you can see why.

```kscript
import KScript.Queue

/// Sends the welcome email to a new user.
job SendWelcome(queue: "mail", tries: 5, backoff: [10s, 1m, 5m]) {
  user_id: int
  email: email

  fn handle() {
    echo "Sending the welcome email to {$email}"
  }

  fn failed(err) {
    echo "Gave up on user {$user_id}: {$err.message}"
  }
}
```

Save it as `jobs/send-welcome.kscript` (`kscript make job SendWelcome` writes one for you). In the editor, a job without `fn handle()` has a quick fix that writes it, and the lightbulb on a job adds `fn failed(err)`. Then, anywhere in the project:

```kscript
import KScript.Queue
import "jobs/send-welcome.kscript"

SendWelcome(user_id: 7, email: 'joan@example.com')->dispatch()
```

And in another terminal:

```sh
kscript worker
```

`job`, `->dispatch()` and the commands below (`@chain`, `@queue`, `@fake_queue`, `@release_job`, `@fail_job`) are part of KScript.Queue, so the file that declares a job and the file that dispatches it both start with `import KScript.Queue`. Nothing is imported silently: without the import, `kscript check` says `job is part of KScript.Queue. Add import KScript.Queue` and the editor offers to add it.

## Declaring a job

A job has a name that starts with a capital letter, its fields, and a `handle` function that does the work. Fields are written as in a [type](types.md): `name: type`, `name?: type` for one that may be left out, and `name: type = default`. Inside `handle` each field is a variable, `$this` is the job, and `$job` says which attempt this is:

| `$job.` | What it is |
| --- | --- |
| `id` | The queued job's id (`null` when it runs with `->run()`). |
| `name` | The job's name, such as `SendWelcome`. |
| `attempt` | This attempt: `1` the first time. |
| `tries` | How many attempts it gets. |
| `queue`, `connection` | Where it came from. |
| `dispatched_at` | When it was dispatched. |

`failed(err)` is optional. It runs once, after the last try has failed, with a `JobFailed` error whose `cause` is the error the last try threw. A job has only these two functions: put helpers outside the job.

A queued job is saved to disk as JSON until a worker runs it, so keep fields small and plain. Pass the user's id, not the user. A `password` field is an error for the same reason.

### Options

| Option | What it does | Default |
| --- | --- | --- |
| `queue` | The queue it goes on. | `default` |
| `connection` | The connection in `kscript.json` it goes to. | `queue.default` |
| `tries` | How many attempts it gets before it fails for good. | `3` |
| `backoff` | How long to wait before each retry: one time (`30s`), a list (`[10s, 1m, 5m]`, the last one repeats) or `'exponential'` (2s, 4s, 8s… up to 10m). | `exponential` |
| `timeout` | The longest one attempt may run. Past it the attempt stops with a `JobTimeout` error. | `60s` |
| `retry_until` | Stop retrying this long after the job was dispatched, even with tries left, such as `1h`. | none |
| `retry_on` | Only retry these error types: `[Network, Timeout]`. Other errors fail the job at once. | every error |
| `fail_on` | Never retry these error types: `[Validation, NotFound]`. | none |
| `unique_for`, `unique_by` | Queue the job only once while one with the same fields is waiting or running, for up to this long: `unique_for: 10m, unique_by: [user_id]`. | none |

Times are durations, written as a number and a unit with no quotes: `500ms`, `30s`, `10m`, `2h`, `1d`. A plain number is seconds. Error types match their children too, so `retry_on: [Network]` also retries an error declared as `error ApiDown extends Network`.

## Dispatching

```kscript
import KScript.Queue
import "jobs/send-welcome.kscript"

SendWelcome(user_id: 7, email: 'joan@example.com')->dispatch()

queued = SendWelcome(user_id: 8, email: 'ann@example.com')->dispatch(delay: 10m, queue: 'low')
echo "Queued {$queued.id} on {$queued.queue}"

SendWelcome(user_id: 9, email: 'bo@example.com')->run()
```

- `Job(...)->dispatch()` queues the job with its own options.
- `->dispatch(delay:, at:, queue:, connection:)` queues it with others. Both return what was queued: `id`, `job`, `queue`, `connection` and `available_at`. `delay` is a duration (`10m`); `at` is a date. It returns `null` for a unique job that is already queued.
- `->run()` runs `handle` here and now, with no queue and no retries. Errors reach the caller.

A job's fields are given by name and checked against its declaration, both by the editor and when it runs.

### Chains

`@chain([...])` runs jobs one after another. Each job is queued when the one before it succeeds, and a job that fails for good stops the chain:

```kscript
import KScript.Queue
import "jobs/orders.kscript"

@chain([ChargeCard(order_id: 42), ShipOrder(order_id: 42), SendReceipt(order_id: 42)])->dispatch()
```

### Inside a handle: @release_job and @fail_job

`@release_job(after: 30s)` puts the job back on its queue to run again later without using up a try, for work that isn't ready yet. `@fail_job(err)` fails the job now, with no more tries, for work that will never succeed. Give it an error (`@fail_job(Validation(message: 'Bad data'))`) or a message.

```kscript
import KScript.Queue

job WaitForPayment(tries: 1) {
  order_id: int
  paid: bool

  fn handle() {
    if not $paid {
      @release_job(after: 1m)
    }
    echo "Order {$order_id} is paid"
  }
}
```

## Connections and drivers

A connection is where jobs wait. `kscript.json` names them under `queue`:

```jsonc
{
  "queue": {
    "default": "file",                 // the connection jobs go to
    "connections": {
      "file": { "driver": "file" },    // these four are always there
      "database": { "driver": "database" },
      "memory": { "driver": "memory" },
      "sync": { "driver": "sync" },
      "uploads": { "driver": "file", "path": "storage/queue" },
      "reports": { "driver": "database", "connection": "reports" }
    },
    "queues": ["high", "default"],     // only these, first to last (default: default, then every other queue)
    "failed": { "keep": "7d" },        // how long failed jobs are kept
    "worker": { "concurrency": 2, "sleep": "1s" },
    "env": {
      "test": { "default": "sync" }    // per environment, as in $config
    }
  }
}
```

| Driver | Where jobs wait | Use it for |
| --- | --- | --- |
| `file` | One JSON file per job in the app's data folder (see [App folders](storage.md)), or in `path`. Workers claim a job by moving its file, so several workers, even in different processes, never run the same job twice. | Most apps. It is the default. |
| `database` | Rows in the project's database (see [Data](data.md#connections)), or in the database `connection` names. | Apps that already have a database, and workers on several machines that share it. |
| `memory` | In memory, in this process only. | `kscript serve --with-worker`, which runs a worker inside the server. |
| `sync` | Nowhere: `->dispatch()` runs the job straight away and its errors reach the caller. | Tests and scripts where waiting isn't worth it. |

### The database driver

The `database` driver keeps waiting and running jobs in a `kscript_jobs` table, failed jobs in `kscript_failed_jobs`, and the keys of unique jobs in `kscript_job_locks`. It makes the tables the first time it needs them, so there is nothing to set up. With no `connection`, it uses the default database: `data/app.sqlite` in the project folder, or `DATABASE_URL`. To keep jobs apart from the app's data, name another database connection from your config:

```jsonc
// kscript.json
{ "queue": { "default": "jobs", "connections": { "jobs": { "driver": "database", "connection": "queue" } } } }

// config.json
{ "database": { "connections": { "queue": { "url": "sqlite:data/queue.sqlite" } } } }
```

A worker takes a job in one write transaction that reads the next due row and marks it as running, so two workers never get the same job. If the database is busy or can't be reached, the worker says so and tries again after a pause, rather than stopping.

If a worker stops in the middle of a job (the machine restarts, say), the job is held for its timeout plus 30 seconds, then goes back on the queue for the next worker, and counts as a try.

## Running workers: kscript worker

```sh
kscript worker                                   # the default connection and queues, until Ctrl+C
kscript worker --queue high,default --concurrency 4
kscript worker --stop-when-empty                 # run what is there, then stop
kscript worker --once                            # one job, for cron
kscript serve --with-worker                      # a worker inside the server
```

The worker prints a line for each job: when it started, whether it finished, how long it took, and when a failed one will be tried again. Ctrl+C lets the running jobs finish first. `--max-jobs` and `--max-time` stop it after a while, so a process manager can start a fresh one. `--connection` picks a connection other than the default. Every option is in [the CLI reference](/cli).

Each attempt runs in a fresh interpreter that loads the job's file again, so a worker picks up a changed job the next time it runs it. A worker that has been running for a while should still be restarted after a deploy, for the settings it read when it started.

## Looking after the queues: kscript queue

```sh
kscript queue status              # waiting, delayed, running and failed jobs per queue
kscript queue list mail           # the waiting jobs with their fields
kscript queue failed              # the failed jobs and their errors
kscript queue retry <id>          # put a failed job back, with its tries reset (or --all)
kscript queue delete <id>         # delete a failed job (or --all)
kscript queue clear mail          # remove every waiting job on a queue
```

Add `--output json` to read the output from another program. From a script, `@queue('mail')` does the same: `->size()`, `->waiting()`, `->failed()`, `->retry(id)` and `->clear()`.

## Testing jobs

`kscript test` runs jobs on the `sync` connection, so a dispatched job runs straight away, unless `queue.env.test` in `kscript.json` says otherwise. To check what a piece of code dispatches without running it, call `@fake_queue()` first:

```kscript
import KScript.Queue
import "jobs/send-welcome.kscript"

queue = @fake_queue()
SendWelcome(user_id: 7, email: 'joan@example.com')->dispatch()

echo "{$queue->count()} welcome email queued for user {$queue->pushed(SendWelcome)[0].user_id}"
$queue->run_all()
```

`->pushed(Job)` lists the dispatched jobs, `->dispatches()` what each dispatch asked for (queue, connection, delay), `->count()` counts them (`->count_of(Job)` those of one job), `->run_all()` runs them here, and `->clear()` forgets them.

## Errors

| Error | When |
| --- | --- |
| `JobTimeout` (a kind of `Timeout`) | An attempt ran longer than the job's `timeout`. |
| `JobFailed` | What `failed(err)` receives, with the last error as `err.cause`. Also what `@fail_job('message')` throws. |

Both can be caught, matched in `retry_on` and `fail_on`, and extended like any other [error type](language.md).
