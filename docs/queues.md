# Queues and jobs: KScript.Queue

## Introduction

Some work shouldn't make a person wait: sending an email, resizing a picture, calling a slow API. KScript.Queue lets you hand that work off as a **job** to run later. `->dispatch()` puts a job on a queue and returns at once. `kscript worker` takes jobs off the queue and runs them. It tries a failed job again, and keeps jobs that keep failing so you can see why.

A job is declared with `job`, its fields and a `handle` function that does the work:

```kscript
import KScript.Queue

/// Sends the welcome email to a new user.
job SendWelcome(queue: "mail", tries: 5) {
  user_id: int
  email: email

  fn handle() {
    echo "Sending the welcome email to {$email}"
  }
}
```

Save it as `jobs/send-welcome.kscript`, and dispatch it from anywhere in the project:

```kscript
import KScript.Queue
import "jobs/send-welcome.kscript"

SendWelcome(user_id: 7, email: 'joan@example.com')->dispatch()
```

Then start a worker in another terminal:

```sh
kscript worker   # runs jobs as they arrive, until Ctrl+C
```

Every file that declares or dispatches a job starts with `import KScript.Queue`. Without it, `kscript check` says `job is part of KScript.Queue. Add import KScript.Queue`, and the editor offers to add it.

## Creating jobs

### Writing a job

`kscript make job SendWelcome` writes a job file for you. A job's name starts with a capital letter. Its fields are written as in a [type](types.md): `name: type`, `name?: type` for one that may be left out, and `name: type = default`.

Inside `handle`, each field is a variable and `$this` is the job. In the editor, a job without `fn handle()` has a quick fix that writes it.

A queued job is saved as JSON until a worker runs it, so keep its fields small and plain. Pass the user's id, not the user. A `password` field is an error for the same reason.

### Knowing which attempt this is

`$job` says which attempt is running, which helps when a job behaves differently on a retry:

```kscript
import KScript.Queue

job SyncContacts(tries: 3) {
  account_id: int

  fn handle() {
    echo "Attempt {$job.attempt} of {$job.tries} for account {$account_id}"
  }
}
```

`$job` has the queued job's `id` (`null` when it runs with `->run()`), its `name`, the `attempt` (`1` the first time), how many `tries` it gets, the `queue` and `connection` it came from, and when it was dispatched (`dispatched_at`).

### Handling a job that fails

Add `fn failed(err)` to do something when a job has failed for good. It runs once, after the last try, with a `JobFailed` error whose `cause` is the error the last try threw. The lightbulb on a job adds it for you:

```kscript
import KScript.Queue

job SendInvoice(tries: 3) {
  invoice_id: int

  fn handle() {
    echo "Sending invoice {$invoice_id}"
  }

  fn failed(err) {
    echo "Gave up on invoice {$invoice_id}: {$err.message}"
  }
}
```

A job has only these two functions. Put helpers outside the job.

## Job options

Options go in the brackets after the job's name. Times are durations, written with no quotes: `500ms`, `30s`, `10m`, `2h`, `1d`. A plain number is seconds.

### Queues and connections

`queue` is the queue the job goes on (`default` unless you say), and `connection` is the connection in `kscript.json` it goes to (`queue.default` unless you say). See [Connections and drivers](#connections-and-drivers).

### Retries and backoff

A job gets 3 tries unless you give `tries`. `backoff` is how long to wait before each retry: one time (`30s`), a list where the last one repeats (`[10s, 1m, 5m]`), or `'exponential'`, the default, which waits 2s, 4s, 8s and so on up to 10m. `retry_until` stops retrying a while after the job was dispatched, even with tries left:

```kscript
import KScript.Queue

job FetchRates(tries: 10, backoff: [10s, 1m, 5m], retry_until: 1h) {
  currency: string

  fn handle() {
    echo "Fetching {$currency} rates"
  }
}
```

### Timeouts

`timeout` is the longest one attempt may run, 60 seconds unless you say. Past it, the attempt stops with a `JobTimeout` error and counts as a try:

```kscript
import KScript.Queue

job BuildReport(timeout: 5m) {
  month: string

  fn handle() {
    echo "Building the report for {$month}"
  }
}
```

### Which errors are retried

By default, every error is retried. `retry_on` retries only the error types you list, and fails the job at once on any other. `fail_on` never retries the types you list. Error types match their children too, so `retry_on: [Network]` also retries an error declared as `error ApiDown extends Network`:

```kscript
import KScript.Queue

job PostWebhook(retry_on: [Network, Timeout], fail_on: [Validation]) {
  url: url

  fn handle() {
    echo "Posting to {$url}"
  }
}
```

### Unique jobs

`unique_for` queues a job only once while one with the same fields is waiting or running, for up to that long. `unique_by` compares only the fields you list:

```kscript
import KScript.Queue

job RebuildSearchIndex(unique_for: 10m, unique_by: [user_id]) {
  user_id: int

  fn handle() {
    echo "Rebuilding the index for user {$user_id}"
  }
}
```

## Dispatching jobs

`->dispatch()` queues a job with its own options, and returns what was queued: its `id`, `job`, `queue`, `connection` and when it's due (`available_at`). It returns `null` for a unique job that is already queued:

```kscript
import KScript.Queue
import "jobs/send-welcome.kscript"

queued = SendWelcome(user_id: 7, email: 'joan@example.com')->dispatch()
echo "Queued {$queued?.id}"
```

A job's fields are given by name, and both the editor and the worker check them against the job's declaration.

### Delaying a job

Give `->dispatch()` a `delay` (a duration) or an `at` (a date) to run the job later, and a `queue` or `connection` to send it somewhere other than its own. Give `delay` or `at`, not both:

```kscript
import KScript.Queue
import "jobs/send-welcome.kscript"

SendWelcome(user_id: 8, email: 'ann@example.com')->dispatch(delay: 10m, queue: 'low')
```

### Running a job now

`->run()` runs `handle` here and now, with no queue and no retries, and its errors reach the caller:

```kscript
import KScript.Queue
import "jobs/send-welcome.kscript"

SendWelcome(user_id: 9, email: 'bo@example.com')->run()
```

On the `sync` connection, `->dispatch()` also runs the job straight away and returns `null`, because nothing was queued. `kscript test` uses `sync`, so code that reads `$queued.id` should allow for `null`, or use [`@fake_queue()`](#testing-jobs).

### Chains

`@chain([...])` runs jobs one after another. Each job is queued when the one before it succeeds, and a job that fails for good stops the chain:

```kscript
import KScript.Queue
import "jobs/orders.kscript"

@chain([ChargeCard(order_id: 42), ShipOrder(order_id: 42), SendReceipt(order_id: 42)])->dispatch()
```

## Controlling a running job

Inside `handle`, `@release_job(after: 30s)` puts the job back on its queue to run again later, without using up a try. Use it for work that isn't ready yet:

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

`@fail_job()` fails the job now, with no more tries, for work that will never succeed. Give it an error or a message:

```kscript
import KScript.Queue

job ImportFile(tries: 5) {
  path: string

  fn handle() {
    if not $path->ends_with('.csv') {
      @fail_job(Validation(message: 'Only CSV files can be imported'))
    }
    echo "Importing {$path}"
  }
}
```

Both work only inside a job's `handle`. Anywhere else they raise a `Context` error. An `exit` in a handle ends that job only: `exit` or `exit 0` finishes it, and any other code fails the attempt like an error, so the worker carries on with the next job.

## Connections and drivers

A connection is where jobs wait, and its driver decides how. You name connections in `kscript.json` under `queue`:

```jsonc
{
  "queue": {
    "default": "file",                 // the connection jobs go to
    "connections": {
      "uploads": { "driver": "file", "path": "storage/queue" },
      "reports": { "driver": "database", "connection": "reports" }
    },
    "queues": ["high", "default"],     // only these, first to last
    "failed": { "keep": "7d" },        // how long failed jobs are kept
    "worker": { "concurrency": 2, "sleep": "1s" },
    "env": {
      "test": { "default": "sync" }    // per environment, as in $config
    }
  }
}
```

Four connections are always there, named after their drivers: `file`, `database`, `memory` and `sync`. Without `queues`, workers take the `default` queue first, then every other queue.

### The file driver

The `file` driver is the default, and suits most apps. It keeps one JSON file per job in the app's data folder (see [App folders](storage.md)), or in the folder `path` names. A worker claims a job by moving its file, so several workers, even in different processes, never run the same job twice.

### The database driver

The `database` driver keeps jobs in the project's database (see [Databases](data.md#connections)). It suits apps that already have a database, and workers on several machines that share it. Waiting and running jobs go in a `kscript_jobs` table, failed jobs in `kscript_failed_jobs`, and the keys of unique jobs in `kscript_job_locks`. It makes the tables the first time it needs them, so there is nothing to set up.

With no `connection`, it uses the default database: `data/app.sqlite` in the project folder, or `DATABASE_URL`. To keep jobs apart from the app's data, name another database connection from your config:

```jsonc
// kscript.json
{ "queue": { "default": "jobs", "connections": { "jobs": { "driver": "database", "connection": "queue" } } } }

// config.json
{ "database": { "connections": { "queue": { "url": "sqlite:data/queue.sqlite" } } } }
```

#### Naming the tables, and making them with migrations

`tables` names the tables a database connection uses. Leave out any you don't want to rename:

```jsonc
// kscript.json
{
  "queue": {
    "connections": {
      "database": { "driver": "database", "tables": { "jobs": "jobs", "failed": "failed_jobs", "locks": "job_locks" } }
    }
  }
}
```

To make the tables with the rest of your [migrations](data.md#migrations), instead of the first time a job is dispatched, publish their migration:

```sh
kscript publish queue                      # the database connection's tables
kscript publish queue --connection jobs    # another database connection's
kscript db migrate
```

`kscript publish queue` writes `database/migrations/<time>_create_queue_tables.kscript`, using the names in `tables`, and sets the connection's `auto_create` to `false`. The driver then leaves the tables to the migration, and says to run `kscript db migrate` if they aren't there yet. Test databases run the migrations, so tests that dispatch to the database queue keep working. A connection that keeps its jobs in another database (`connection`) leaves its tables to the driver, because `kscript db migrate` runs on the default database.

A worker takes a job in one write transaction that reads the next due row and marks it as running, so two workers never get the same job. If the database is busy or can't be reached, the worker says so and tries again after a pause, rather than stopping.

### The memory and sync drivers

The `memory` driver keeps jobs in memory, in one process only. `kscript serve --with-worker` uses it to run a worker inside the server.

The `sync` driver doesn't queue anything: `->dispatch()` runs the job straight away, and its errors reach the caller. It suits tests, and scripts where waiting isn't worth it.

## Running workers

`kscript worker` runs jobs from the default connection and queues until you press Ctrl+C:

```sh
kscript worker                                        # the default connection and queues, until Ctrl+C
kscript worker --queue high,default --concurrency 4   # high before default, four jobs at a time
kscript worker --stop-when-empty                      # run what is there, then stop
kscript worker --once                                 # one job, for cron
kscript serve --with-worker                           # a worker inside the server
```

The worker prints a line for each job: when it started, whether it finished, how long it took, and when a failed one will be tried again. Ctrl+C lets the running jobs finish first. `--max-jobs` and `--max-time` stop the worker after a while, so a process manager can start a fresh one, and `--connection` picks a connection other than the default. Every option is in [the CLI reference](/cli).

Each attempt runs in a fresh interpreter that loads the job's file again, so a worker picks up a changed job the next time it runs it. Still restart a long-running worker after a deploy, for the settings it read when it started.

If a worker stops in the middle of a job, such as when the machine restarts, the job is held for its timeout plus 30 seconds. It then goes back on the queue for the next worker, and counts as a try.

## Managing queues

`kscript queue` shows what is waiting and what has failed, and lets you retry or remove jobs. Use a job ID from `kscript queue failed` in place of `42`:

```sh
kscript queue status              # waiting, delayed, running and failed jobs per queue
kscript queue list mail           # the waiting jobs with their fields
kscript queue failed              # the failed jobs and their errors
kscript queue retry 42            # put a failed job back, with its tries reset (or --all)
kscript queue delete 42           # delete a failed job (or --all)
kscript queue clear mail          # remove every waiting job on a queue
```

Add `--output json` to read the output from another program. A script may do the same with `@queue()`:

```kscript
import KScript.Queue

mail = @queue('mail')
echo "{ $mail->size() } emails waiting, { $mail->failed()->count() } failed"
```

`@queue()` also has `->waiting()`, `->retry(id)` and `->clear()`.

## Testing jobs

`kscript test` runs jobs on the `sync` connection, so a dispatched job runs straight away, unless `queue.env.test` in `kscript.json` says otherwise. To check what a piece of code dispatches without running it, call `@fake_queue()` first:

```kscript
import KScript.Queue
import "jobs/send-welcome.kscript"

queue = @fake_queue()
SendWelcome(user_id: 7, email: 'joan@example.com')->dispatch()

echo "{$queue->count_of(SendWelcome)} welcome email queued for user {$queue->pushed(SendWelcome)[0].user_id}"
$queue->run_all()
```

The fake queue's `->pushed(Job)` lists the dispatched jobs, and `->dispatches()` lists what each dispatch asked for (queue, connection, delay). `->count()` counts them, and `->count_of(Job)` counts those of one job. `->run_all()` runs them here, and `->clear()` forgets them.

## Errors

A job that runs past its `timeout` throws `JobTimeout`, a kind of `Timeout`. `failed(err)` receives a `JobFailed`, with the last error as `err.cause`, and `@fail_job('message')` throws one too. Both may be caught, listed in `retry_on` and `fail_on`, and extended like any other [error type](/reference/errors).
