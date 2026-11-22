# Databases: KScript.Data

KScript.Data keeps records in a database. A `model` is a type whose records live in a table: it checks every value by its field's type and rules, like any [type](types.md), and gives you a query builder whose conditions are ordinary lambdas, turned into SQL.

```kscript
import KScript.Data

enum Role { member, editor, admin }

model User(timestamps, unique: [email], hidden: [password], guarded: [role]) {
  name: string(max: 120)
  email: email
  age?: int
  role: Role = 'member'
  password: password
}

$db->create_table(User)

joan = User->create(name: 'Joan', email: 'joan@example.com', age: 34, password: 'correct horse battery')
adults = User->where(fn($u) => $u.age >= 18 and $u.email->ends_with('@example.com'))->order_by(name)->get()
echo $adults->map(fn($u) => $u.name)
echo $joan.id
```

The database is SQLite, in `data/app.sqlite` in your project folder (the folder with `kscript.json`, or the script's folder). Nothing needs installing: KScript ships SQLite. Postgres, MySQL and MariaDB are a url away: see [Connections](#connections).

## Models

A model is declared like a [type](types.md), with `model` instead of `type`. Its fields are the table's columns.

```kscript
import KScript.Data

/// A post on the blog.
model Post(timestamps, soft_deletes, index: [published]) {
  title: string(max: 200)
  body: string
  published: bool = false
  tags: string[] = []

  fn excerpt() => $body->truncate(80)
}

$db->create_table(Post)
post = Post->create(title: 'Hello', body: 'The first post.')
echo $post->excerpt()
```

A model's options go in the brackets after its name, like the options of every other declaration: `model Post(table: "posts", timestamps, unique: [slug])`. The block holds only fields, `validate` rules and `fn`. An option written as a line of the block still runs, but `kscript check` reports it (KS6014) and `kscript check --fix` moves it into the brackets.

| Option | What it does |
| --- | --- |
| `table: "posts"` | The table. Default: the plural of the name in snake case (`User` is `users`, `BlogPost` is `blog_posts`). |
| `key: "slug"` | The field that is the key. Default: `id`, an `int` the database numbers, added for you. |
| `timestamps` | Adds `created_at` and `updated_at`, set when records are saved. |
| `soft_deletes` | Adds `deleted_at`. `->delete()` sets it instead of removing the row, and queries leave those records out. |
| `unique: [email, [team_id, slug]]` | Fields no two records may share. A list in the list is a set of fields that is unique together. A save that clashes fails with `UniqueViolation`. |
| `index: [team_id]` | Fields to index, so queries on them stay fast. |
| `hidden: [password]` | Fields left out of JSON, so they never reach a route's response. |
| `guarded: [role]` | Fields `->create($form)` and `->update($form)` leave alone, so a form can't set them. Set them on the record: `$user.role = 'admin'`. |
| `connection: "reports"` | The [connection](#connections) the table is in. |

How each type is stored:

| Field type | Column |
| --- | --- |
| `string`, `email`, `handle`, `uuid` | Text. Emails compare without minding case, so `JOAN@example.com` finds `joan@example.com`. |
| `int`, `number`, `float` | Integer or real. |
| `bool` | 0 or 1. |
| `date` | Text in ISO order, so dates sort and compare. |
| an enum | Text, checked by the database to be one of the enum's words. |
| `password` | The password's hash (Argon2id). It is never stored as typed. |
| lists, maps and other types | JSON. |

An optional field (`age?: int`) is a column that can be null. A computed field (`label => $first + ' ' + $last`) isn't stored: it is worked out each time a record is read.

## Adding and saving

```kscript
import KScript.Data

model Note(timestamps) {
  text: string
  pinned: bool = false
}

$db->create_table(Note)

note = Note->create(text: 'Buy milk')
$note.pinned = true
$note->save()

$note->update(text: 'Buy oat milk')
echo $note->is_dirty()
echo Note->count()
```

| Method | What it does |
| --- | --- |
| `Model->create(values)` | Checks the values, saves a new record and returns it. Takes a map (`->create($form)`) or names and values. |
| `Model(values)` | Calling the model makes a record without saving it, the way calling any type makes a value: `note = Note(text: 'Buy milk')`. `$note->save()` adds it. |
| `Model->first_or_create(match, values)` | The first record matching `match`, or a new one made from both maps. |
| `$record->save()` | Saves a record: adds it if it's new, otherwise writes only the fields that changed (and `updated_at`). |
| `$record->update(values)` | Sets fields and saves. |
| `$record->delete()` | Deletes it (with `soft_deletes`, marks it deleted). `->force_delete()` always removes it, and `->restore()` brings a soft-deleted one back. |
| `$record->fresh()` | The record as the database has it now. |
| `$record->is_dirty(field?)`, `->dirty()` | Whether it has changes that aren't saved, and which fields. |

Values are checked as they are set: `$note.pinned = 'maybe'` fails with a Type error before anything reaches the database.

## Queries

Every query starts from the model, and each step returns a new query, so a query can be built up and reused.

```kscript
import KScript.Data

model Order(timestamps) {
  customer: string
  total: number
  status: string = 'open'
}

$db->create_table(Order)
Order->insert([
  { customer: 'Ada', total: 120 },
  { customer: 'Ben', total: 35, status: 'paid' },
  { customer: 'Ada', total: 60, status: 'paid' },
])

big = 50
paid = Order->where(status: 'paid')
echo $paid->count()
echo $paid->sum(total)
echo Order->where(fn($o) => $o.total > $big)->order_by(total: desc)->pluck(customer)
echo Order->where(fn($o) => $o.customer->starts_with('A'))->latest()->first().total
```

### Conditions

`->where()` takes a lambda whose parameter is the row: `$o.total` is a column. The rest of the lambda (variables, function calls, literals) is worked out first and sent to the database as a parameter, so a value can never change the SQL.

These parts can be turned into SQL: comparisons (`==`, `!=`, `<`, `<=`, `>`, `>=`), `and`, `or`, `not`, `in` with a list, `??`, arithmetic, `? :`, and these string methods on a column: `->contains()`, `->starts_with()`, `->ends_with()`, `->lower()`, `->upper()`, `->trim()`, `->length()` and `->matches(regex)`. `$o.x == null` is `IS NULL`. Anything else is a `QueryError` that says which part can't run in the database, rather than a query that quietly does something else.

Names and values are a shorter way to write equality: `->where(status: 'paid', customer: 'Ada')`. A list means any of them (`status: [open, paid]`), and `null` means the column is empty. Enum words don't need quotes: `->where(role: admin)`.

| Method | What it does |
| --- | --- |
| `->where(...)`, `->or_where(...)`, `->where_not(...)` | Keep the rows that match, or that match these instead, or that don't. |
| `->when(test, fn($q) => ...)` | Changes the query only when the test is true, for optional filters. |
| `->order_by(name)`, `->order_by(total: desc)` | Sorts. `->latest()` and `->oldest()` sort by `created_at`. |
| `->limit(n)`, `->offset(n)` | Reads at most `n` rows, after skipping `n`. |
| `->select(id, name)`, `->distinct()` | Reads only these columns, and leaves out rows that repeat. |
| `->with_trashed()`, `->only_trashed()` | With `soft_deletes`: keep the deleted records too, or read only them. |
| `->lock_for_update()`, `->skip_locked()` | In a transaction, lock the rows read (and skip ones another transaction has locked). SQLite locks the whole database for writes, so these change nothing there. |

### Reading

| Method | What it gives |
| --- | --- |
| `->get()`, `Model->all()` | A list of records. |
| `->first()`, `->find(id)` | One record, or null. |
| `->first_or_fail()`, `->find_or_fail(id)` | One record, or a `ModelNotFound` error, which a route answers with 404. |
| `->count()`, `->exists()` | How many rows match, and whether any do. |
| `->sum(col)`, `->avg(col)`, `->min(col)`, `->max(col)` | Totals over the rows that match. |
| `->pluck(col)`, `->value(col)` | One column of every row, or of the first. |
| `->paginate(per_page, page)` | One page: `{ items, page, per_page, total, last_page, from, to, has_more }`. `page` may be text, such as `$query.page`. |
| `->chunk(size, fn($rows, $n) => ...)` | Goes through the rows a batch at a time, for more rows than fit in memory. Return `false` to stop. |
| `->sql()` | The SQL and parameters the query would run, without running it. |

### Changing many rows

`->update(values)`, `->increment(col, by)`, `->decrement(col, by)`, `->delete()`, `->force_delete()` and `->restore()` change every row that matches, in one statement, and return how many rows changed. `Model->insert(rows)` adds a list of rows, each checked like `->create()`.

## Transactions

A transaction keeps a group of changes together: when the block ends they are all saved, and if anything in it fails none of them are. The error then carries on.

```kscript
import KScript.Data

model Account {
  owner: string
  balance: number = 0
}

$db->create_table(Account)
Account->create(owner: 'Ada', balance: 100)
Account->create(owner: 'Ben')

fn transfer(from: int, to: int, amount: number) {
  transaction {
    Account->where(id: $from)->decrement(balance, by: $amount)
    Account->where(id: $to)->increment(balance, by: $amount)
    if Account->find($from).balance < 0 {
      throw Conflict(message: 'Not enough money')
    }
  }
}

@transfer(1, 2, 30)
try {
  @transfer(1, 2, 500)
} catch Conflict as e {
  echo $e.message
}
echo Account->pluck(balance)
```

`@transaction(fn() => ...)` does the same for a function and returns what it returns. A transaction inside another is a savepoint, so only its own changes are undone. `retries: 3` tries again when the database is busy.

## Raw SQL

`@sql` runs SQL you write. Each `{ }` in it is worked out and sent as a parameter, never pasted into the SQL, so it is safe with any value. A list becomes `(?, ?, ?)` for `in`. Write `{{` and `}}` for braces.

Write the SQL inside the call, as below. Text in `"double quotes"` given to a variable first is filled in there and then, so its values would be pasted into the SQL; the editor warns about that. To keep SQL in a variable, use `'single quotes'` or a ```` ```sql ```` block.

```kscript
import KScript.Data

$db->sql('create table if not exists visits (page text, at text)')
page = '/about'
$db->sql("insert into visits (page, at) values ({$page}, {@now()->to_string()})")
echo @sql("select count(*) as n from visits where page = {$page}")[0].n
```

A query gives its rows as maps (or as a type: `@sql(text, as: User)`), and anything else gives how many rows it changed. `@db('visits')` is a query on a table with no model, whose rows are maps.

## Connections

With no settings, the database is `data/app.sqlite` in the project folder, or the `DATABASE_URL` environment variable when it is set. Set connections in your config:

```yaml
database:
  default: main
  connections:
    main:
      url: ${DATABASE_URL:-postgres://app:secret@localhost:5432/app}
    reports:
      url: sqlite:data/reports.sqlite
```

A model uses another connection with `connection: reports`, and `@db(connection: reports)` is that database.

| Database | url | Notes |
| --- | --- | --- |
| SQLite | `sqlite:data/app.sqlite`, or `sqlite::memory:` | A file, relative to the project folder. `:memory:` lasts while kscript runs. |
| Postgres | `postgres://user:password@host:5432/app` | Also `postgresql://`. Version 12 or later. |
| MySQL | `mysql://user:password@host:3306/app` | Version 8.0.16 or later. |
| MariaDB | `mariadb://user:password@host:3306/app` | Version 10.6 or later. |

Anything after `?` goes to the driver as it is, such as `?sslmode=require` for Postgres or `?SslMode=Required` for MySQL. Write characters such as `@` or `:` in a password as `%40` and `%3A`. Instead of a url, a server connection can list its parts:

```yaml
database:
  connections:
    main:
      driver: postgres
      host: db.example.com
      port: 5432
      database: app
      username: app
      password: ${DB_PASSWORD}
      options: { sslmode: require }
```

Models, queries, transactions and the database queue work the same on each database:

- Text compares exactly, and emails don't mind case, so `JOAN@example.com` finds `joan@example.com` and a unique email can't be taken twice in different case. On Postgres, `->create_table()` makes a case-insensitive collation for emails, `kscript_nocase`; on MySQL and MariaDB, tables use `utf8mb4_bin` and emails `utf8mb4_unicode_ci`.
- `->contains()`, `->starts_with()` and `->ends_with()` don't mind case on any database, and `->matches()` reads its flags (`i`, `m`, `s`, `x`) the same everywhere.
- Nulls sort first going up and last going down.
- `->lock_for_update()` and `->skip_locked()` lock rows on Postgres, MySQL and MariaDB. SQLite locks the whole database for a transaction's writes instead.
- Dates are kept to the microsecond.

Raw SQL in `@sql` is given to the database as it is, so it is written in that database's own SQL.

## Errors

| Error | When | In a route |
| --- | --- | --- |
| `ModelNotFound` (extends `NotFound`) | `->find_or_fail()` and `->first_or_fail()` find nothing. `$e.model` and `$e.id` say what. | 404 |
| `UniqueViolation` (extends `Conflict`) | A save would give two records the same unique value. `$e.fields` names the fields. | 409 |
| `ForeignKeyViolation` (extends `Conflict`) | A change would leave a row pointing at one that isn't there. | 409 |
| `QueryError` | The database refused a query, or a condition can't be turned into SQL. | 500 |
| `ConnectionError` | The database can't be opened. | 503 |

## Making a model

```
kscript make model User --fields "name: string, email: email, password: password" --timestamps
```

writes `models/user.kscript` (the folder is `paths.models` in `kscript.json`).
