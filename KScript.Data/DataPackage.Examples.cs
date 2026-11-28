using KScript.Runtime;

namespace KScript.Data
{
    /// <summary>
    /// The examples the reference shows for each model, query, record and database method. Each one is a whole script
    /// that the docs can run: it declares its models and makes their tables in the docs sandbox's own database.
    /// </summary>
    public sealed partial class DataPackage
    {
        /// <summary>A model with soft deletes and three tasks, for the query examples.</summary>
        private const string Tasks =
            "<model name=\"Task\" fields=\"title: string, done: bool = false, points: int = 1\" soft_deletes=\"true\" unique=\"title\" />\n" +
            "<do>$db->create_table(Task); Task->insert([{ title: 'Write docs', points: 3 }, { title: 'Fix bug', done: true, points: 2 }, { title: 'Ship it', points: 5 }])</do>\n";

        /// <summary>Authors and their posts, for the relation examples.</summary>
        private const string Authors =
            "<model name=\"Author\" fields=\"name: string, posts: Post[]\" />\n" +
            "<model name=\"Post\" fields=\"title: string, published: bool = false, author: Author\" />\n" +
            "<do>$db->create_table(Author); $db->create_table(Post)</do>\n" +
            "<let name=\"ada\" value=\"Author->create(name: 'Ada')\" />\n" +
            "<do>Post->create(title: 'Notes', published: true, author: $ada); Post->create(title: 'Draft', author: $ada)</do>\n";

        /// <summary>Posts and tags, linked many to many, for the pivot examples.</summary>
        private const string Tags =
            "<model name=\"Post\" fields=\"title: string, tags: Tag[]\" />\n" +
            "<model name=\"Tag\" fields=\"name: string, posts: Post[]\" />\n" +
            "<do>$db->create_table(Post); $db->create_table(Tag)</do>\n" +
            "<let name=\"post\" value=\"Post->create(title: 'Hello')\" />\n" +
            "<let name=\"news\" value=\"Tag->create(name: 'news')\" />\n" +
            "<let name=\"tips\" value=\"Tag->create(name: 'tips')\" />\n";

        private const string Accounts =
            "<model name=\"Account\" fields=\"email: email, password: password\" />\n" +
            "<do>$db->create_table(Account)</do>\n" +
            "<let name=\"account\" value=\"Account->create(email: 'joan@example.com', password: 'correct horse')\" />\n";

        /// <summary>Examples for the methods a model and a query share, with the same example for both.</summary>
        private static readonly (string Name, string Example)[] SharedQueryExamples =
        {
            ("where", Tasks + "<echo>{ Task->where(done: false)->pluck(title) }</echo>\n<echo>{ Task->where(fn(t) => $t.points >= 3)->count() }</echo>"),
            ("or_where", Tasks + "<echo>{ Task->where(done: true)->or_where(points: 5)->pluck(title) }</echo>"),
            ("where_not", Tasks + "<echo>{ Task->where_not(done: true)->pluck(title) }</echo>"),
            ("when", Tasks + "<let name=\"search\" value=\"'docs'\" />\n<echo>{ Task->when($search != null, fn(q) => $q->where(fn(t) => $t.title->contains($search)))->pluck(title) }</echo>"),
            ("select", Tasks + "<let name=\"task\" value=\"Task->select(id, title)->first()\" />\n<echo>{ $task.title }: { $task.points ?? 'not read' }</echo>"),
            ("distinct", Tasks + "<echo>{ Task->select(done)->distinct()->count() }</echo>"),
            ("order_by", Tasks + "<echo>{ Task->order_by(points: desc)->pluck(title) }</echo>\n<echo>{ Task->order_by(done, title)->pluck(title) }</echo>"),
            ("latest", Tasks + "<echo>{ Task->latest(points)->value(title) }</echo>"),
            ("oldest", Tasks + "<echo>{ Task->oldest(points)->value(title) }</echo>"),
            ("limit", Tasks + "<echo>{ Task->order_by(title)->limit(2)->pluck(title) }</echo>"),
            ("offset", Tasks + "<echo>{ Task->order_by(title)->offset(1)->limit(1)->pluck(title) }</echo>"),
            ("with_trashed", Tasks + "<do>Task->where(done: true)->delete()</do>\n<echo>{ Task->count() } of { Task->with_trashed()->count() }</echo>"),
            ("only_trashed", Tasks + "<do>Task->where(done: true)->delete()</do>\n<echo>{ Task->only_trashed()->pluck(title) }</echo>"),
            ("lock_for_update", Tasks + "<transaction>\n  <let name=\"next\" value=\"Task->where(done: false)->order_by(id)->lock_for_update()->first()\" />\n  <do>$next->update(done: true)</do>\n</transaction>\n<echo>{ Task->where(done: true)->count() } done</echo>"),
            ("skip_locked", Tasks + "<transaction>\n  <let name=\"next\" value=\"Task->where(done: false)->order_by(id)->lock_for_update()->skip_locked()->first()\" />\n  <echo>{ $next.title }</echo>\n</transaction>"),
            ("get", Tasks + "<for each=\"task\" in=\"Task->where(done: false)->order_by(title)->get()\">\n  <echo>{ $task.title }: { $task.points }</echo>\n</for>"),
            ("first", Tasks + "<let name=\"task\" value=\"Task->order_by(points)->first()\" />\n<echo>{ $task.title }</echo>\n<echo>{ Task->where(points: 99)->first() ?? 'none' }</echo>"),
            ("first_or_fail", Tasks + "<try>\n  <do>Task->where(points: 99)->first_or_fail()</do>\n  <catch type=\"ModelNotFound\" as=\"e\">\n    <echo>{ $e.type }</echo>\n  </catch>\n</try>"),
            ("find", Tasks + "<echo>{ Task->find(1).title }</echo>\n<echo>{ Task->find(99) ?? 'none' }</echo>"),
            ("find_or_fail", Tasks + "<try>\n  <do>Task->find_or_fail(99)</do>\n  <catch type=\"ModelNotFound\" as=\"e\">\n    <echo>{ $e.type }: { $e.message }</echo>\n  </catch>\n</try>"),
            ("exists", Tasks + "<echo>{ Task->where(done: true)->exists() } { Task->where(points: 99)->exists() }</echo>"),
            ("count", Tasks + "<echo>{ Task->count() } tasks, { Task->where(done: false)->count() } to do</echo>"),
            ("sum", Tasks + "<echo>{ Task->sum(points) } points, { Task->where(done: false)->sum(points) } left</echo>"),
            ("average", Tasks + "<echo>{ Task->average(points)->round(2) }</echo>"),
            ("min", Tasks + "<echo>{ Task->min(points) }</echo>"),
            ("max", Tasks + "<echo>{ Task->max(points) }</echo>"),
            ("pluck", Tasks + "<echo>{ Task->order_by(title)->pluck(title) }</echo>"),
            ("value", Tasks + "<echo>{ Task->where(done: true)->value(title) }</echo>"),
            ("paginate", Tasks + "<let name=\"page\" value=\"Task->order_by(id)->paginate(2, page: 1)\" />\n<echo>{ $page.items->map(fn(t) => $t.title) }: page { $page.page } of { $page.last_page }, { $page.total } in all</echo>"),
            ("cursor_paginate", Tasks + "<let name=\"page\" value=\"Task->cursor_paginate(2)\" />\n<echo>{ $page.items->map(fn(t) => $t.title) }, more: { $page.has_more }</echo>\n<let name=\"rest\" value=\"Task->cursor_paginate(2, after: $page.next)\" />\n<echo>{ $rest.items->map(fn(t) => $t.title) }, more: { $rest.has_more }</echo>"),
            ("chunk", Tasks + "<fn name=\"show\" params=\"batch: list, n: int\">\n  <echo>{ $n }: { $batch->map(fn(t) => $t.title)->join(', ') }</echo>\n</fn>\n<do>Task->order_by(id)->chunk(2, @show)</do>"),
            ("update", Tasks + "<echo>{ Task->where(done: false)->update(done: true) } changed</echo>\n<echo>{ Task->where(done: true)->count() } done</echo>"),
            ("increment", Tasks + "<do>Task->where(title: 'Ship it')->increment(points, by: 2)</do>\n<echo>{ Task->where(title: 'Ship it')->value(points) }</echo>"),
            ("decrement", Tasks + "<do>Task->where(title: 'Ship it')->decrement(points)</do>\n<echo>{ Task->where(title: 'Ship it')->value(points) }</echo>"),
            ("delete", Tasks + "<echo>{ Task->where(done: true)->delete() } deleted</echo>\n<echo>{ Task->count() } left, { Task->with_trashed()->count() } kept to restore</echo>"),
            ("force_delete", Tasks + "<echo>{ Task->where(done: true)->force_delete() } removed</echo>\n<echo>{ Task->with_trashed()->count() } left in the table</echo>"),
            ("restore", Tasks + "<do>Task->where(done: true)->delete()</do>\n<echo>{ Task->only_trashed()->restore() } restored, { Task->count() } tasks</echo>"),
            ("insert", Tasks + "<echo>{ Task->insert([{ title: 'Plan' }, { title: 'Review', points: 2 }]) } added</echo>\n<echo>{ Task->count() } tasks</echo>"),
            ("upsert", Tasks + "<do>Task->upsert({ title: 'Ship it', points: 8 }, key: 'title'); Task->upsert({ title: 'Plan', points: 1 }, key: 'title')</do>\n<echo>{ Task->where(title: 'Ship it')->value(points) } points, { Task->count() } tasks</echo>"),
            ("sql", Tasks + "<let name=\"query\" value=\"Task->where(done: false)->order_by(title)->sql()\" />\n<echo>{ $query.sql }</echo>\n<echo>{ $query.params->to_json() }</echo>"),
            ("with", Authors + "<for each=\"post\" in=\"Post->with(author)->order_by(title)->get()\">\n  <echo>{ $post.title } by { $post.author.name }</echo>\n</for>"),
            ("with_count", Authors + "<let name=\"author\" value=\"Author->with_count(posts)->first()\" />\n<echo>{ $author.name } has { $author.posts_count } posts</echo>"),
        };

        private static void RegisterExamples(PackageBuilder p)
        {
            foreach (var (name, example) in SharedQueryExamples)
                p.Examples(("model->" + name, example), ("query->" + name, example));

            p.Examples(
                // On a relation: $post->tags() is a query.
                ("query->create", Authors + "<let name=\"post\" value=\"$ada->posts()->create(title: 'Hello')\" />\n<echo>{ $post.title } by { $post.author.name }</echo>"),
                ("query->attach", Tags + "<echo>{ $post->tags()->attach([$news, $tips]) } linked</echo>\n<echo>{ $post->tags()->order_by(name)->pluck(name) }</echo>"),
                ("query->detach", Tags + "<do>$post->tags()->attach([$news, $tips])</do>\n<echo>{ $post->tags()->detach($news) } unlinked</echo>\n<echo>{ $post->tags()->pluck(name) }</echo>"),
                ("query->sync", Tags + "<do>$post->tags()->attach($news)</do>\n<let name=\"changes\" value=\"$post->tags()->sync([$tips])\" />\n<echo>linked { $changes.attached }, unlinked { $changes.detached }</echo>\n<echo>{ $post->tags()->pluck(name) }</echo>"),
                ("query->toggle", Tags + "<do>$post->tags()->attach($news)</do>\n<do>$post->tags()->toggle([$news, $tips])</do>\n<echo>{ $post->tags()->pluck(name) }</echo>"),

                // On a model.
                ("model->create", "<model name=\"Note\" fields=\"text: string, pinned: bool = false\" />\n<do>$db->create_table(Note)</do>\n<let name=\"note\" value=\"Note->create(text: 'Buy milk')\" />\n<echo>{ $note.id }: { $note.text }, pinned: { $note.pinned }</echo>"),
                ("model->all", Tasks + "<echo>{ Task->all()->map(fn(t) => $t.title) }</echo>"),
                ("model->first_or_create", Tasks + "<let name=\"plan\" value=\"Task->first_or_create({ title: 'Plan' }, { points: 2 })\" />\n<let name=\"again\" value=\"Task->first_or_create({ title: 'Plan' })\" />\n<echo>{ $plan.points } points; same record: { $again.id == $plan.id }; { Task->count() } tasks</echo>"),
                ("model->query", Tasks + "<let name=\"query\" value=\"Task->query()\" />\n<let name=\"query\" value=\"$query->where(done: false)\" />\n<echo>{ $query->count() } to do</echo>"),
                ("model->fake", "<model name=\"Person\" fields=\"name: string, email: email\" />\n<do>$db->create_table(Person)</do>\n<let name=\"joan\" value=\"Person->fake(name: 'Joan')\" />\n<echo>{ $joan.name } { $joan.email->ends_with('@example.com') }</echo>"),
                ("model->fake_many", "<model name=\"Person\" fields=\"name: string, email: email\" />\n<do>$db->create_table(Person)</do>\n<let name=\"people\" value=\"Person->fake_many(3)\" />\n<echo>{ $people->count() } made, { Person->count() } saved</echo>"),

                // On a record.
                ("record->save", Tasks + "<let name=\"task\" value=\"Task->find(1)\" />\n<do>task.done = true</do>\n<do>$task->save()</do>\n<echo>{ Task->find(1).done }</echo>"),
                ("record->update", Tasks + "<let name=\"task\" value=\"Task->find(1)\" />\n<do>$task->update(points: 8)</do>\n<echo>{ Task->find(1).points }</echo>"),
                ("record->delete", Tasks + "<do>Task->find(2)->delete()</do>\n<echo>{ Task->count() } left, { Task->with_trashed()->count() } in the table</echo>"),
                ("record->force_delete", Tasks + "<do>Task->find(2)->force_delete()</do>\n<echo>{ Task->with_trashed()->count() } in the table</echo>"),
                ("record->is", Tasks + "<let name=\"task\" value=\"Task->find(1)\" />\n<echo>{ $task->is(Task->first()) } { $task->is(2) }</echo>"),
                ("record->restore", Tasks + "<let name=\"task\" value=\"Task->find(2)\" />\n<do>$task->delete()</do>\n<do>$task->restore()</do>\n<echo>{ Task->count() } tasks</echo>"),
                ("record->fresh", Tasks + "<let name=\"task\" value=\"Task->find(1)\" />\n<do>Task->where(title: 'Write docs')->update(points: 9)</do>\n<echo>{ $task.points }, then { $task->fresh().points }</echo>"),
                ("record->is_dirty", Tasks + "<let name=\"task\" value=\"Task->find(1)\" />\n<do>task.points = 4</do>\n<echo>{ $task->is_dirty() } { $task->is_dirty('title') }</echo>"),
                ("record->dirty", Tasks + "<let name=\"task\" value=\"Task->find(1)\" />\n<do>task.points = 4; task.done = true</do>\n<echo>{ $task->dirty() }</echo>"),
                ("record->load", Authors + "<let name=\"post\" value=\"Post->first()\" />\n<do>$post->load(author)</do>\n<echo>{ $post.author.name }</echo>"),
                ("record->is_saved", Tasks + "<let name=\"task\" value=\"Task->find(1)\" />\n<echo>{ $task->is_saved() }</echo>"),

                // A password field's value.
                ("password_hash->verify", Accounts + "<echo>{ $account.password->verify('correct horse') } { $account.password->verify('wrong') }</echo>"),
                ("password_hash->needs_rehash", Accounts + "<echo>{ $account.password->needs_rehash('bcrypt') }</echo>"),

                // $db and the other connections.
                ("database->table", Tasks + "<echo>{ $db->table('tasks')->where(done: false)->count() } rows</echo>"),
                ("database->sql", Tasks + "<let name=\"rows\" value=\"$db->sql('select title from tasks order by points')\" />\n<echo>{ $rows->map(fn(r) => $r.title) }</echo>"),
                ("database->tables", Tasks + "<echo>{ $db->tables() }</echo>"),
                ("database->describe_table", Tasks + "<let name=\"table\" value=\"$db->describe_table(Task)\" />\n<echo>{ $table.columns->map(fn(c) => $c.name) }</echo>"),
                ("database->has_table", "<model name=\"Note\" fields=\"text: string\" />\n<echo>{ $db->has_table(Note) }</echo>\n<do>$db->create_table(Note)</do>\n<echo>{ $db->has_table('notes') }</echo>"),
                ("database->create_table", "<model name=\"Note\" fields=\"text: string\" />\n<echo>{ $db->create_table(Note) } { $db->create_table(Note) }</echo>"),
                ("database->drop_table", Tasks + "<echo>{ $db->drop_table(Task) } { $db->has_table('tasks') }</echo>"),
                ("database->transaction", Tasks + "<do>$db->transaction(fn() => Task->where(done: false)->update(done: true))</do>\n<echo>{ Task->where(done: true)->count() } done</echo>"),

                ("@db", Tasks + "<echo>{ @db('tasks')->where(done: false)->pluck(title) }</echo>"),
                ("@sql", Tasks + "<let name=\"least\" value=\"3\" />\n<let name=\"rows\" value='@sql(\"select title from tasks where points >= {$least} order by title\")' />\n<echo>{ $rows->map(fn(r) => $r.title) }</echo>"),
                ("@queries", Tasks + "<do>Task->where(done: false)->count()</do>\n<echo>{ @queries()->last().sql }</echo>"));
        }
    }
}
