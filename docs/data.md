# Databases: KScript.Data

## Introduction

KScript.Data lets you keep records in a database and read them back without writing SQL. You describe each table as a **model**: a [type](types.md) whose records are stored as rows. KScript.Data checks every value against its field's type before it reaches the database, and turns your queries into SQL for you.

Your first model needs nothing set up. SQLite ships with KScript, and the database is the file `data/app.sqlite` in your project folder (the folder with `kscript.json`, or the script's own folder). When you're ready for Postgres, MySQL or MariaDB, you only change a URL: see [Connections](#connections).

To get started, import the package and declare a model:

```kscript
import KScript.Data

model Note {
  text: string
}

$db->create_table(Note)

Note->create(text: 'Buy milk')
echo Note->count()
```

`$db->create_table()` makes the table, which is all a script needs. A project whose tables already hold data changes them with [migrations](#migrations) instead.

## Models

### Defining a model

A model is declared like a [type](types.md), with `model` in place of `type`. Each field becomes a column of the table:

```kscript
import KScript.Data

model Post {
  title: string(max: 200)
  body: string
  published: bool = false
  tags: string[] = []
}
```

A field with a default, such as `published`, may be left out when a record is made. A field marked with `?` may be empty (null):

```kscript
import KScript.Data

model User {
  name: string
  age?: int
}
```

A model may also have methods. Inside a method, `$this` and the fields belong to the record it was called on:

```kscript
import KScript.Data

model Post {
  title: string
  body: string

  fn excerpt() => $body->truncate(80)
}
```

Like every declaration, a model may have a `///` doc comment above it, which the editor shows when you hover over its name.

### Timestamps and soft deletes

Two things many tables want come as mixins that KScript.Data ships. A model extends them, as it would any other [base](#extending-models).

#### Timestamps

KScript.Data ships a `Timestamps` mixin. A model that extends it gets `created_at` and `updated_at` fields. Both are filled in when a record is first saved, and `updated_at` changes every time it is saved again:

```kscript
import KScript.Data

model Comment extends Timestamps {
  text: string
}
```

#### Soft deletes

A model that extends the `SoftDeletes` mixin doesn't lose a row when a record is deleted. Instead, its `deleted_at` field is set, and queries leave it out from then on. You may still read deleted records with `->with_trashed()`, and bring one back with `->restore()`:

```kscript
import KScript.Data

model Task extends SoftDeletes {
  title: string
}

$db->create_table(Task)
task = Task->create(title: 'Write the report')
$task->delete()

echo Task->count()
echo Task->with_trashed()->count()
```

### Model options

Options go in brackets after the model's name. An option that is simply on is written as a bare word, and the rest take a value:

```kscript
import KScript.Data

model Post(auto_create, unique: [slug]) {
  title: string
  slug: string
}
```

When the list grows long, you may put each option on its own line. A trailing comma is allowed, and the formatter keeps it, so adding another option changes only one line:

```kscript
import KScript.Data

model Attachment(
  auto_create,
  unique: [name],
) {
  name: string
}
```

The block itself holds only fields, `validate` rules and `fn` methods. An option written as a line of the block is reported as KS6014, and `kscript check --fix` moves it into the brackets.

#### Table names and keys

By default, a model's table is the plural of its name in snake case: `User` is stored in `users`, and `BlogPost` in `blog_posts`. You may name the table yourself with `table:`:

```kscript
import KScript.Data

model Person(table: 'people') {
  name: string
}
```

Every model has a key. Unless you say otherwise, it is an `int` field called `id`, numbered by the database and added for you. To use one of your own fields instead, name it with `key:`:

```kscript
import KScript.Data

model Country(key: 'code') {
  code: string(max: 2)
  name: string
}
```

#### Unique fields and indexes

`unique:` lists fields that no two records may share. A list inside the list is a set of fields that must be unique together. A save that would break the rule fails with a `UniqueViolation` error:

```kscript
import KScript.Data

model Member(unique: [email, [team_id, handle]]) {
  email: email
  team_id: int
  handle: string
}
```

`index:` lists fields you often search or sort by, so those queries stay fast as the table grows:

```kscript
import KScript.Data

model Order(index: [customer, [status, created_at]]) extends Timestamps {
  customer: string
  status: string
}
```

#### Protecting fields

Two options keep fields safe when records come from, or go to, the outside world. `hidden:` leaves fields out of JSON, so they never appear in a route's response. `guarded:` lists fields that `->create($form)` and `->update($form)` won't set, so a form can't change them:

```kscript
import KScript.Data

model Account(hidden: [password], guarded: [role]) {
  email: email
  password: password
  role: string = 'member'
}
```

A guarded field may still be set directly on the record, as in `account.role = 'admin'`.

#### Extra keys

When `->create()` is given a key the model doesn't have, it drops that key. The `extra:` option changes this: it may keep the keys or make them an error instead.

#### Connections and creating tables

`connection:` puts the model's table in another [connection](#connections), such as `connection: reports`. `auto_create` creates the table and its indexes the first time the model is used, if the table isn't there yet. It suits small apps. For tables that change over time, use [migrations](#migrations).

Every option is also listed in the reference: [Models and mixins](/reference/models).

### Extending models

Models that share fields, methods or options can take them from a common base with `extends`. A base that is only there to be extended, with no table of its own, is a **mixin**, marked with the `mixin` option:

```kscript
import KScript.Data

/// Things that can be published.
model Publishable(mixin, index: [published_at]) extends Timestamps {
  published_at?: date

  fn published(query) => $query->where(fn(p) => $p.published_at != null)
}

model Page(auto_create) extends Publishable {
  title: string(min: 1, max: 200)
}

Page->create(title: 'About', published_at: @now())
echo Page->published()->count()
```

A model may extend several bases at once. List them in brackets, and later bases take precedence over earlier ones:

```kscript
import KScript.Data

/// Gives a model a uid as its key.
model UID(mixin, key: 'uid') {
  uid: uuid = @uuid()
}

/// Things that can be published.
model Publishable(mixin) extends Timestamps {
  published_at?: date
}

model Invoice extends [UID, Publishable] {
  total: number
}
```

A model takes its bases' fields, `validate` rules, methods, [scopes](#scopes) and options such as `key`, `unique`, `index`, `hidden`, `guarded` and `extra`. A base may itself extend the built-in mixins, so a model that extends `Publishable` above has timestamps too. A field or method of the model itself replaces one of the same name, and the model's own options win over all of its bases'.

Some things are never inherited. Each model has its own `table`, `connection` and `auto_create`, because each concrete model has its own table. A model that extends a mixin isn't a mixin itself unless it says so.

You may change a base's option for one use by giving it in brackets after the base's name:

```kscript
import KScript.Data

model Publishable(mixin, hidden: [published_at]) {
  published_at?: date
}

model Draft extends Publishable(hidden: []) {
  text: string
}
```

In XML, the list of bases goes in the model's `with` attribute.

### How fields are stored

Each field type is stored in the column type that suits it, so you rarely need to think about it. A few are worth knowing:

- `string`, `email`, `handle` and `uuid` are text. Emails compare without minding case, so `JOAN@example.com` finds `joan@example.com`.
- `int`, `number` and `float` are integers or real numbers, and `bool` is 0 or 1.
- `date` is text in ISO order, so dates sort and compare correctly.
- An enum is text, and the database checks that it is one of the enum's words.
- A `password` is stored as its hash (Argon2id), never as typed.
- A field whose type is another model, such as `author: User`, is a [relation](#relations). It is stored as an `author_id` column with a foreign key to `users`.
- Lists, maps and any other type are stored as JSON.

A computed field, such as `label => $first + ' ' + $last`, isn't stored at all. It is worked out each time a record is read.

## Built-in models and mixins

Some packages ship mixins you may extend straight away. Import the package, then extend the mixin. You don't declare it yourself. A model may extend several at once: `model Post extends [Timestamps, SoftDeletes] { ... }`.

| Mixin | Package | What a model that extends it gets |
| --- | --- | --- |
| `Timestamps` | KScript.Data | `created_at` and `updated_at`, set when records are saved. See [Timestamps](#timestamps). |
| `SoftDeletes` | KScript.Data | `deleted_at`: `->delete()` marks a record deleted, and queries leave it out. See [Soft deletes](#soft-deletes). |
| `Ownable` | KScript.Auth | An `owner` field, set from whoever is signed in (or the browser's guest) and never changed after, the `->mine()` scope and `->is_mine()`. See [Guests and owners](auth.md#guests-and-owners). |

For example, notes that belong to one person and know when they were written:

```kscript
import KScript.Data
import KScript.Auth

model Note extends [Ownable, Timestamps] {
  text: string
}
```

Then `Note->mine()->get()` reads only the current person's notes. In `kscript mcp`, `lookup Ownable` shows a mixin's source.

The [Models and mixins](/reference/models) reference lists every mixin with its fields and methods, every option with an example, and the tables KScript keeps in your database for itself (`kscript_migrations` and the database queue's `kscript_jobs`, `kscript_failed_jobs` and `kscript_job_locks`).

KScript.Auth can also keep its users in a model of your own. See [Users in a model](auth.md#users-in-a-model).

## Creating and updating records

### Creating records

To add a record, call `->create()` on the model with its fields. The values are checked, the record is saved, and you get it back with its key filled in:

```kscript
import KScript.Data

model Note extends Timestamps {
  text: string
  pinned: bool = false
}

$db->create_table(Note)

note = Note->create(text: 'Buy milk')
echo $note.id
```

`->create()` also takes a map, which is handy for a form: `Note->create($form)`.

If you would like to make a record without saving it yet, call the model like a function. The record is saved when you call `->save()`:

```kscript
note = Note(text: 'Buy bread')
$note->save()
```

Sometimes you want a record only if it isn't there already. `->first_or_create()` finds the first record that matches its first map, or makes one from both maps:

```kscript
tag = Tag->first_or_create({ name: 'urgent' }, { colour: 'red' })
```

To add many rows at once, pass a list to `->insert()`. Each row is checked the same way `->create()` checks one:

```kscript
Note->insert([
  { text: 'Call the bank' },
  { text: 'Water the plants', pinned: true },
])
```

### Updating records

Change a record's fields, then call `->save()`. Only the fields that changed are written, along with `updated_at`:

```kscript
note = Note->find(1)
note.pinned = true
$note->save()
```

`->update()` sets fields and saves in one step:

```kscript
$note->update(text: 'Buy oat milk')
```

Values are checked as soon as you set them. `note.pinned = 'maybe'` fails with a `Type` error before anything reaches the database.

You may ask a record whether it has unsaved changes with `->is_dirty()`, optionally for one field, and `->dirty()` names the fields that changed. `->fresh()` reads the record again, as the database has it now.

### Deleting records

`->delete()` removes a record. On a model that extends `SoftDeletes`, it marks the record deleted instead. Use `->force_delete()` to remove it for good, and `->restore()` to bring a soft-deleted record back:

```kscript
$note->delete()
```

## Queries

### Retrieving records

Every query starts from the model. `->all()` gives every record, and `->get()` runs a query you've built:

```kscript
import KScript.Data

model Order extends Timestamps {
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

for order in Order->where(status: 'paid')->get() {
  echo "{$order.customer}: {$order.total}"
}
```

Each step returns a new query, so you may build a query up and reuse it:

```kscript
paid = Order->where(status: 'paid')
echo $paid->count()
echo $paid->where(customer: 'Ada')->count()
```

To read one record, use `->find()` with a key or `->first()` with a query. Both give `null` when nothing matches:

```kscript
order = Order->find(1)
latest = Order->where(customer: 'Ada')->latest()->first()
```

When a missing record is a mistake, use `->find_or_fail()` or `->first_or_fail()` instead. They throw a `ModelNotFound` error, which a route answers with a 404 for you.

If you only need one column, `->pluck()` gives it for every row and `->value()` gives it for the first:

```kscript
names = Order->pluck(customer)
```

### Conditions

The simplest condition names fields and the values they must equal:

```kscript
open_for_ada = Order->where(status: 'open', customer: 'Ada')->get()
```

A list means any of its values, and `null` means the field is empty. Enum words don't need quotes:

```kscript
active = User->where(role: [editor, admin])->get()
no_age = User->where(age: null)->get()
```

For anything else, pass a lambda. Its parameter stands for the row, so `$o.total` is a column:

```kscript
big = 50
large_orders = Order->where(fn(o) => $o.total > $big and $o.status != 'paid')->get()
```

The lambda is turned into SQL. Everything in it that isn't a column, such as `$big`, is worked out first and sent to the database as a parameter, so a value can never change the SQL. When there is only one condition on the row, you may leave out the lambda and start with a dot:

```kscript
a_customers = Order->where(.customer->starts_with('A'))->get()
```

A condition may use comparisons, `and`, `or`, `not`, `in` with a list, `??`, arithmetic and `? :`. On a column, it may also call `->contains()`, `->starts_with()`, `->ends_with()`, `->lower()`, `->upper()`, `->trim()`, `->count()` and `->matches(regex)`. Anything else is a `Query` error that says which part can't run in the database, rather than a query that quietly does something different.

A few rules keep conditions working the way they do in a script. `$o.x == null` is SQL's `IS NULL`, even when the `null` comes from a variable. `$o.status != 'paid'` keeps rows whose status is empty. And `/` divides as it does in a script, so two whole-number columns can give 3.5.

`->or_where()` adds another way to match, and `->where_not()` keeps the rows that don't match:

```kscript
urgent = Order->where(status: 'open')->or_where(fn(o) => $o.total > 100)->get()
unpaid = Order->where_not(status: 'paid')->get()
```

`->when()` changes a query only when its test is true, which is useful for optional filters, such as a search box that may be empty:

```kscript
results = Order->when($search != '', fn(q) => $q->where(.customer->contains($search)))->get()
```

### Ordering, limiting and paging

`->order_by()` sorts by a field, ascending unless you say `desc`:

```kscript
biggest_first = Order->order_by(total: desc)->get()
```

`->latest()` and `->oldest()` sort by `created_at`, or by the key when the model doesn't extend `Timestamps`. Both also take a field name.

`->limit()` reads at most that many rows, and `->offset()` skips rows first. `->select()` reads only some columns, and `->distinct()` leaves out rows that repeat.

To show results a page at a time, use `->paginate()`. It gives one page of records along with what a page of links needs: `items`, `page`, `per_page`, `total`, `last_page`, `from`, `to` and `has_more`. The page number may be text, straight from the query string:

```kscript
page = Order->latest()->paginate(20, $query.page)
```

### Counting and totals

`->count()` and `->exists()` ask the database how many rows match, or whether any do, without reading the rows:

```kscript
echo Order->where(status: 'open')->count()
```

`->sum()`, `->average()`, `->min()` and `->max()` total a column over the rows that match:

```kscript
echo Order->where(status: 'paid')->sum(total)
```

### Scopes

A query you write often deserves a name. A **scope** is a model method whose first parameter is `query`. It adds to the query it's given and returns it, so more steps can follow:

```kscript
import KScript.Data

model Article {
  title: string
  views: int = 0
  published: bool = false

  fn live(query) => $query->where(published: true)
  fn popular(query, over: int = 100) => $query->where(fn(a) => $a.views > $over)
}

$db->create_table(Article)
echo Article->live()->popular(over: 10)->count()
```

You may call a scope on the model, on a query, or on a [relation](#scopes-on-relations). Its other parameters work as they do in any function. A scope from a [mixin](#extending-models) becomes the model's own, and `kscript check` knows a scope wherever it's called from, including other files.

### Changing many rows

Some methods change every row a query matches in a single statement, and give back how many rows changed. They are `->update()`, `->increment()`, `->decrement()`, `->delete()`, `->force_delete()` and `->restore()`:

```kscript
Order->where(status: 'open')->update(status: 'cancelled')
Article->where(published: true)->increment(views, by: 1)
```

These can't follow `->limit()` or `->offset()`. That is an `Argument` error rather than a change to every row. To change only some rows, read their keys first: `->where(id: $query->pluck(id))`.

### Chunking results

When a query matches more rows than fit comfortably in memory, `->chunk()` goes through them a batch at a time. Return `false` from the function to stop early:

```kscript
fn send(batch: list, n: int) {
  echo "Batch {$n}: {$batch->count()} orders"
}

Order->chunk(100, @send)
```

### Seeing the SQL

`->sql()` gives the SQL a query would run, and its parameters, without running it. It's a quick way to check what a condition turns into.

## Relations

### Defining relations

Relations need no extra declarations. A field whose type is another model links the two, and KScript.Data works out the kind of link from the fields on both sides:

```kscript
import KScript.Data

model User {
  name: string
  posts: Post[](by: author)
  profile?: Profile
}

model Profile {
  user: User
  bio: string
}

model Post {
  title: string
  published: bool = false
  author: User
  tags: Tag[]
}

model Tag {
  name: string
  posts: Post[]
}
```

Here is what each kind of field means.

**Belongs to.** `author: User` on Post stores an `author_id` column, a foreign key to `users`. The model gets an `author_id` field too.

**Has one.** `profile?: Profile` on User, where Profile has `user: User`, is kept as `user_id` on `profiles`. The side that has one is the optional side.

**Has many.** `posts: Post[]` on User, where Post has a field that is a User, uses that field's column on `posts`. When Post has more than one such field (say `author` and `editor`), name the one you mean with `by:`, as in `posts: Post[](by: author)`.

**Many to many.** `tags: Tag[]` on Post, where Tag has a list of Posts rather than one, is kept in a pivot table named `post_tag`, with `post_id` and `tag_id`. Name the table yourself with `pivot:`.

**Has many through.** `comments: Comment[](through: posts)` reads the comments of this user's posts, through another relation. It stores nothing of its own.

A relation may also keep only the records one of the other model's [scopes](#scopes) keeps: `drafts: Post[](by: author, scope: drafts)`.

On a belongs-to field, `on_delete:` says what happens when the record it points to is deleted. `cascade` deletes this record too, and is the default for a required field. `set_null` empties the field, and is the default for an optional one. `restrict` refuses the delete with a `ForeignKeyViolation` error.

Two models that refer to each other may each live in their own file without importing the other. When a field names a model that isn't imported, it's found in the project's models folder (`paths.models` in `kscript.json`). This is the one place a model is found without an import, because otherwise the two files would import each other, and circular imports aren't allowed. Scripts and controllers that use a model still import its file. See [one type per file](projects.md#where-files-go-the-recommended-layout).

### Reading relations

To read a relation, read it like a field. You get one record (or `null`) for belongs to and has one, and a list for the others:

```kscript
ada = User->create(name: 'Ada')
post = $ada->posts()->create(title: 'Notes on the engine')

echo $post.author.name
echo $ada.posts->map(.title)
```

When you read a relation on records that came from the same list, it's loaded for the whole list at once. A loop like this one makes one query for all the authors, not one for each post:

```kscript
for post in Post->latest()->get() {
  echo "{$post.title} by {$post.author.name}"
}
```

### Loading relations up front

`->with()` loads relations together with the records. A dotted name loads a relation's own relations:

```kscript
users = User->with(profile, posts.tags)->get()
```

If you only want some of the related records, give a function that changes the relation's query:

```kscript
users = User->with(posts: fn(q) => $q->where(published: true))->get()
```

`->load()` does the same for a record you already have:

```kscript
$post->load(author, tags)
```

A relation that hasn't been loaded is left out of JSON, so a route's response includes only the relations you loaded.

To count related records without reading them, use `->with_count()`. Each record gets a `posts_count` field, counted by the database:

```kscript
authors = User->with_count(posts)->get()
```

### Querying relations

Calling a relation as a method, such as `$ada->posts()`, gives a query of the related records. You may filter it, count it, or add to it, and everything stays within that user's posts:

```kscript
import KScript.Data

echo $ada->posts()->where(published: false)->count()
$ada->posts()->create(title: 'A second post')
```

`->create()` on a relation sets the link for you: the new post's author is Ada.

A many-to-many relation is linked through its pivot table. `->attach()` links records (or keys, or a list of either), keeping links that are already there. `->detach()` unlinks the records you give, or all of them. `->sync()` links exactly the records you give and unlinks the rest, and `->toggle()` flips each one:

```kscript
import KScript.Data

maths = Tag->create(name: 'maths')
$post->tags()->attach($maths)
$post->tags()->sync([$maths])
```

A belongs-to relation is set on the record itself. `post.author = $ben` sets `author_id`, and `$post->save()` writes it.

### Relations in conditions

A condition may read through relations, and the database does the work:

```kscript
busy = User->where(fn(u) => $u.posts->count() > 10)->get()
tagged = Post->where(fn(p) => $p.tags->any(fn(t) => $t.name == 'maths'))->get()
by_ada = Post->where(fn(p) => $p.author.name == 'Ada')->get()
no_profile = User->where(fn(u) => $u.profile == null)->get()
```

On a list relation, `->count()`, `->any()`, `->is_empty()`, `->all()`, `->sum()`, `->average()`, `->min()` and `->max()` become SQL, each with an optional lambda on the related record. On a single related record, you may read its fields, compare it with a record, or compare it with `null`. Comparing with a record also works with the short form:

```kscript
mine = Post->where(author: $ada)->get()
```

### Scopes on relations

A [scope](#scopes) works on a relation too, and keeps to the relation's records: `$ada->posts()->live()->count()` counts only Ada's live posts.

## Transactions

A transaction keeps a group of changes together. When the block ends, they're all saved. If anything inside fails, none of them are, and the error carries on:

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

for amount in [30, 500] {
  try {
    @transfer(1, 2, $amount)
  } catch Conflict as err {
    echo $err.message
  }
}
echo Account->pluck(balance)
```

If you'd rather wrap a function, `$db->transaction(fn() => ...)` does the same and returns what the function returns. A transaction inside another becomes a savepoint, so only its own changes are undone. Pass `retries: 3` to try again when the database is busy.

Inside a transaction, `->lock_for_update()` locks the rows a query reads, and `->skip_locked()` skips rows another transaction has locked. Both lock rows on Postgres, MySQL and MariaDB. SQLite locks the whole database for a transaction's writes instead, so they change nothing there.

## Raw SQL

When you need SQL that a query can't express, `@sql` runs SQL you write. Each `{ }` inside it is worked out and sent as a parameter, never pasted into the SQL, so it's safe with any value:

```kscript
import KScript.Data

$db->sql('create table if not exists visits (page text, at text)')
page = '/about'
$db->sql("insert into visits (page, at) values ({$page}, {@now()->to_string()})")
echo @sql("select count(*) as n from visits where page = {$page}")[0].n
```

A list becomes `(?, ?, ?)`, for use with `in`. Write `{{` and `}}` for literal braces.

A query gives its rows as maps. If you'd like records of a type instead, pass it: `@sql(text, as: User)`. Any other statement gives how many rows it changed.

Write the SQL inside the call, as above. Text in double quotes that is given to a variable first is filled in at that moment, so its values would be pasted into the SQL. The editor warns you when that happens. To keep SQL in a variable, use single quotes or a ```` ```sql ```` text block.

For a table with no model, `@db('visits')` is a query whose rows are maps.

## Connections

### Configuring connections

With no settings, the database is `data/app.sqlite` in the project folder, or the `DATABASE_URL` environment variable when it's set. To use other databases, list connections under `database` in the `config` section of `kscript.json`:

```json
{
  "config": {
    "database": {
      "default": "main",
      "connections": {
        "main": { "url": "postgres://app:secret@localhost:5432/app" },
        "reports": { "url": "sqlite:data/reports.sqlite" }
      }
    }
  }
}
```

A model uses another connection with the `connection: reports` option, and `@db(connection: reports)` gives you that database.

Each database has its own URL:

- SQLite: `sqlite:data/app.sqlite`, a file relative to the project folder, or `sqlite::memory:`, which lasts while kscript runs.
- Postgres 12 or later: `postgres://user:password@host:5432/app` (or `postgresql://`).
- MySQL 8.0.16 or later: `mysql://user:password@host:3306/app`.
- MariaDB 10.6 or later: `mariadb://user:password@host:3306/app`.

In a URL, put driver options after `?`, as in `?sslmode=require`, and percent-encode characters such as `@` and `:` in passwords.

You may also give a connection as a map of fields. Settings that come from environment variables go in `config.kscript`, which is laid over `kscript.json` (see [Settings](language.md#settings-config)):

```kscript
fn configure(environment: string) {
  url = @env('DATABASE_URL', default: '')
  main = {
    driver: 'postgres'
    host: @env('DB_HOST', default: 'localhost')
    port: @env('DB_PORT', default: '5432')
    database: @env('DB_NAME', default: 'app')
    username: @env('DB_USER', default: 'app')
    password: @env('DB_PASSWORD', default: '')
  }
  if !$url->is_blank() {
    main = { url: $url }
  }
  return { database: { connections: { main: $main } } }
}
```

A password given as a field may contain `@` and `:` as they are. Driver options go in `options:`, such as `options: { sslmode: 'require' }`.

### Differences between databases

Models, queries, transactions and the database queue work the same on every database. KScript.Data smooths over the differences that would otherwise surprise you:

- Text compares exactly, but emails don't mind case, so `JOAN@example.com` finds `joan@example.com` and a unique email can't be taken twice in different case. On Postgres, `->create_table()` makes a case-insensitive collation for emails, `kscript_nocase`. On MySQL and MariaDB, tables use `utf8mb4_bin` and emails use `utf8mb4_unicode_ci`.
- `->contains()`, `->starts_with()` and `->ends_with()` don't mind case on any database, and `->matches()` reads its flags (`i`, `m`, `s`, `x`) the same way everywhere.
- Nulls sort first going up and last going down.
- Dates are kept to the microsecond.

Raw SQL in `@sql` is passed to the database as it is, so write it in that database's own dialect.

### Time zones

A date is stored as a clock time in the project's time zone: `"timezone"` in `kscript.json`, which is `Europe/London` unless you set another, such as `America/New_York`, `UTC` or any IANA name. A date is converted to that zone when it's written and back when it's read, so a row means the same moment on a laptop in one zone and a server in another.

You should choose the time zone before the project has data. Rows already stored keep the clock time they were written with. A database written before this setting existed holds the clock times of the computer that wrote it, so set `"timezone"` to that computer's zone (`"UTC"` for most servers and containers).

## Errors

KScript.Data throws errors you can catch by type. In a route, each one becomes a sensible HTTP status for you:

- `ModelNotFound`, a kind of `NotFound`, comes from `->find_or_fail()` and `->first_or_fail()`. `$e.model` and `$e.id` say what wasn't found. A route answers 404.
- `UniqueViolation`, a kind of `Conflict`, means a save would give two records the same unique value. `$e.fields` names the fields. A route answers 409.
- `ForeignKeyViolation`, also a `Conflict`, means a change would leave a row pointing at one that isn't there. A route answers 409.
- `Query` means the database refused a query, or a condition can't be turned into SQL. A route answers 500.
- `Connection` means the database can't be opened. A route answers 503.

```kscript
try {
  User->create(email: 'ada@example.com')
} catch UniqueViolation as e {
  echo "That email is already taken."
}
```

## Migrations

### Introduction to migrations

`$db->create_table()` is enough for a script. A project whose tables already hold rows changes them with **migrations** instead. Each migration is a file in `database/migrations` (`paths.migrations` in `kscript.json`) that says, step by step, how the tables change.

You don't usually write migrations yourself. Change your models, then let `kscript db diff` write the migration that takes the tables from where the migrations leave them to what the models want:

```sh
kscript db diff              # Write a migration from the changes to your models
kscript db diff add_bio      # The same, named add_bio
kscript db diff --pretend    # Print the migration instead of writing it
kscript db migrate           # Run the migrations that haven't run
```

A migration it writes looks like this:

```kscript
// Written by kscript db diff from the models.
import KScript.Data

Migration {
  CreateTable(name: 'users') {
    Field(name: 'id', type: int, key, auto)
    Field(name: 'name', type: string, max: 80)
    Field(name: 'email', type: email)
    Field(name: 'role', type: enum, values: [member, admin], default: 'member')
    Index(columns: [email], unique)
  }
  AddField(table: 'posts', name: 'views', type: int, default: 0)
}
```

When a table or column has gone and another has appeared, `diff` asks whether it was renamed, so a rename keeps its rows instead of dropping them. Where it can't ask, such as in CI or with its input piped in, it assumes no, drops and adds, and warns about each one. You should always read a migration before you run it.

To write a migration by hand, `kscript make migration add_bio_to_users` gives you an empty one to fill in.

### Migration steps

Migration steps call a table's columns fields, as models do.

`CreateTable(name:) { ... }` makes a table, with a `Field` for each column and an `Index` for each index. A key made of two columns, such as a pivot table's, is `Key(columns: [a, b])`. `DropTable(name:)` drops a table and its rows, and `RenameTable(from:, to:)` renames one. Foreign keys that point to a renamed table follow it.

`AddField(table:, name:, type:, ...)` adds a field. A required field needs a `default:` when the table has rows. `DropField(table:, name:)` drops a field and its indexes, and `RenameField(table:, from:, to:)` renames one, keeping its values. `ChangeField(table:, name:, type:, ...)` changes a field to match what you describe: its type, whether it's optional, its default or what it references. The rows are kept.

`AddIndex(table:, columns:)` adds an index, with `unique` for a unique one, and `DropIndex(...)` drops it.

A field's `type:` is one of `string`, `int`, `number`, `bool`, `date`, `email`, `handle`, `uuid`, `password`, `json` or `enum` (with `values:`), each stored the way a model's field of that type is. `max:` gives a string a length, `optional` lets it be empty, and `default:` is what the database fills in. The `key` and `auto` flags make a numbered key, and `references:` (with `references_column:` and `on_delete:`) makes a foreign key, like a model's relation field.

For anything the other steps can't do, `Sql(up:, down:)` runs SQL as it is.

Every step is checked against the tables the earlier migrations make before anything runs. Dropping a table that isn't there, adding a column twice, or pointing at a column that isn't a key tells you which migration and what's wrong, with the closest name when it looks like a typo.

### Running and rolling back

```sh
kscript db migrate            # Run what hasn't run, as one batch
kscript db migrate --pretend  # Print the SQL instead of running it
kscript db rollback           # Undo the last batch
kscript db rollback --steps 2 # Undo the last two migrations
kscript db status             # Show what has run, and what the models want that no migration makes
kscript db fresh              # Drop every table and run every migration again
kscript db fresh --pretend    # Name the tables it would drop and print the SQL, changing nothing
```

On SQLite and Postgres, each migration runs in a transaction, so one that fails changes nothing. MySQL and MariaDB commit each change to a table as it's made, so a migration that fails there stops where it failed. The error says how many of its steps ran, and the migration isn't marked as run.

Rolling back undoes each step the other way round. A created table is dropped, an added column is dropped, and a dropped table or column comes back as it was, empty. A `Sql` step needs its `down:` for that. When you'd rather say the steps yourself, add a `Down { ... }` block, and rollback runs that block instead. The migrations that have run are recorded in the `kscript_migrations` table, with the batch each ran in.

In the production environment (`kscript db --env production`, or `KSCRIPT_ENV=production`), `migrate`, `rollback` and `seed` ask before they start, and `fresh` won't run without `--force`.

`diff` and `status` see the models on the default connection, in the models folder and [inside components](components.md#types-that-belong-to-a-component). Models on another connection, and changes made by a `Sql` step, are left out, so after a `Sql` step that changes a table, `diff` may suggest changing it back.

### Seeders

A seeder is a script in `database/seeders` (`paths.seeders`) that fills the database with records for development and tests:

```kscript
import KScript.Data
import "../../models/user.kscript"

User->create(name: 'Ada', email: 'ada@example.com')
```

`kscript db seed` runs every seeder in name order, and `kscript db seed users` runs one. `kscript db migrate --seed` and `kscript db fresh --seed` run them after migrating. `kscript make seeder users` writes a new one.

## Testing with a database

Under `kscript test`, each test file that uses KScript.Data gets its own database in memory, made from your migrations (or from the models, when there are none yet). Each test's changes are undone when it ends. Your tests don't need `$db->create_table()`, never touch the project's real database, and never see each other's records:

```kscript
// tests/post.test.kscript
import KScript.Data
import "../models/post.kscript"

test(name: "a post starts with no views") {
  post = Post->fake(title: 'Hello')
  expect($post.views, equals: 0)
  expect(Post->count(), equals: 1)
}

test(name: "each test starts empty") {
  expect(Post->count(), equals: 0)
}
```

If you'd rather test against the configured database, set `test.database` in `kscript.json` to `config`. Each test is still undone when it ends. See `test.env` in [Testing](testing.md#settings-in-kscriptjson).

### Fakes

`Model->fake()` saves a record with made-up values for every field you don't give. `->fake_many()` saves several:

```kscript
joan = Author->fake(name: 'Joan')
posts = Post->fake_many(3, author: $joan)
```

The made-up values fit each field's type and rules: a `string(min: 3, max: 12)` is 3 to 12 characters, an `email` ends in `@example.com`, and an enum is its first value. A required relation gets a fake record of its own.

### Counting queries

A page that sends one query per row gets slow as the data grows. `@queries()` is the SQL sent so far in the test, each as `{ sql, bindings, database }`, so you may check how many queries a page needs:

```kscript
test(name: "the post list loads authors at once") {
  Post->fake_many(10)
  Post->with(author)->get()
  expect(@queries(), count_at_most: 2)
}
```

## Making a model

`kscript make model` writes a model file for you, in the models folder (`paths.models` in `kscript.json`):

```sh
kscript make model User --fields "name: string, email: email, password: password" --timestamps  # Write models/user.kscript
```

## With the rest of KScript

Models work with the rest of KScript without any glue:

- **Routes.** A controller parameter typed as a model, such as `fn show(post: Post)`, is the record whose key is in the path, or a 404. See [Controllers](http.md).
- **Components.** An input with the default `@inject()`, such as `construct({ post: Post = @inject() })`, takes the record the route holds, so a page may write `<post-card />`. See [components](components.md#take-the-routes-record-with-inject).
- **Policies.** `policy for Post { fn update(user: User, post: Post) => ... }` says who may create, read, update and delete a model's records. Ask it with `@can(update, $post)`, `@authorize(update, $post)`, `can: update` on a route, or `@inject(can: read)`. See [policies](auth.md#policies).
- **OpenAPI.** A model in a route's input or answer becomes a schema. The key and timestamps are read-only, a relation appears only when it was loaded with `->with()`, and `->paginate()` answers with a page of records.
- **Sign-in.** `http.auth.users` keeps [KScript.Auth](auth.md#users-in-a-model)'s users in a model, so `@user()` is a record.

## In the editor

The language server knows every model's fields. It completes them in `->where(`, `->order_by(`, `->create(` and the rest, and relations in `->with(`. A field the model doesn't have is reported as KS5013 (see [errors](errors.md)). A `where` lambda's parameter is the model's record, so `fn(p) => $p.` completes its fields too.

Hover over a query method to see roughly the SQL the chain sends, with `?` for the values:

```sql
SELECT * FROM posts
WHERE views > ? AND author_id = ?
ORDER BY created_at DESC
LIMIT ?
```

When the project has migrations, a model that the migrations don't make as it is now is reported as KS5014, with the first change `kscript db diff` would write, such as `add field users.bio`.
