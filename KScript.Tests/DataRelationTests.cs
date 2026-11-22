using KScript.Data;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>Relations: fields whose types are models, loaded when read or with ->with(), and used in query lambdas.</summary>
public sealed class DataRelationTests : DataFixture
{
    private const string Blog = """
        model User {
          name: string
          email: email
          posts: Post[](by: author)
          profile?: Profile
          comments: Comment[](through: posts)
          drafts: Post[](by: author, scope: drafts)
        }

        model Profile {
          user: User
          bio: string
        }

        model Post(soft_deletes) {
          title: string
          published: bool = false
          views: int = 0
          author: User
          editor?: User(on_delete: set_null)
          tags: Tag[]
          comments: Comment[]

          fn drafts(query) => $query->where(published: false)
          fn popular(query, over: int = 10) => $query->where(fn(p) => $p.views > $over)
        }

        model Tag {
          name: string
          posts: Post[]
        }

        model Comment {
          post: Post
          body: string
        }

        $db->create_table(User)
        $db->create_table(Profile)
        $db->create_table(Post)
        $db->create_table(Tag)
        $db->create_table(Comment)

        ada = User->create(name: 'Ada', email: 'ada@example.com')
        ben = User->create(name: 'Ben', email: 'ben@example.com')
        cy = User->create(name: 'Cy', email: 'cy@example.com')
        Profile->create(user: $ada, bio: 'Maths')
        engines = $ada->posts()->create(title: 'Engines', views: 50)
        notes = Post->create(title: 'Notes', author: $ada, published: true, views: 5)
        hello = Post->create(title: 'Hello', author_id: $ben.id, published: true, views: 20, editor: $ada)
        maths = Tag->create(name: 'maths')
        history = Tag->create(name: 'history')
        $engines->tags()->attach([$maths, $history])
        $notes->tags()->attach($history.id)
        $engines->comments()->create(body: 'Nice')
        $engines->comments()->create(body: 'Long')
        $notes->comments()->create(body: 'Ok')

        """;

    [Fact]
    public void EachKindIsWorkedOutFromTheFields()
    {
        string[] lines = Lines(Blog + """
            echo $db->tables()
            echo $engines.author.name
            echo $ada.profile.bio
            echo $ben.profile ?? 'none'
            echo $ada.posts->map(fn(p) => $p.title)
            echo $engines.tags->map(fn(t) => $t.name)
            echo $history.posts->map(fn(p) => $p.title)
            echo $ada.comments->map(fn(c) => $c.body)
            echo $ada.drafts->map(fn(p) => $p.title)
            echo $hello.editor.name
            echo $notes.editor ?? 'no editor'
            echo [$engines.author_id, $hello.author_id]
            """);
        Assert.Equal(new[] { "comments", "post_tag", "posts", "profiles", "tags", "users" }, lines[0].Split(", ").Order(StringComparer.Ordinal));
        Assert.Equal("Ada", lines[1]);
        Assert.Equal("Maths", lines[2]);
        Assert.Equal("none", lines[3]);
        Assert.Equal("Engines, Notes", lines[4]);
        Assert.Equal("maths, history", lines[5]);
        Assert.Equal("Engines, Notes", lines[6]);
        Assert.Equal("Nice, Long, Ok", lines[7]);
        Assert.Equal("Engines", lines[8]);
        Assert.Equal("Ada", lines[9]);
        Assert.Equal("no editor", lines[10]);
        Assert.Equal("1, 2", lines[11]);
    }

    [Fact]
    public void TheMeanIsAverageOnRelationsAndQueries()
    {
        string[] lines = Lines(Blog + """
            echo User->where(fn(u) => $u.posts->average(fn(p) => $p.views) > 25)->pluck(name)
            echo Post->average(views)
            echo $ada->posts()->average(views)
            """);
        Assert.Equal("Ada", lines[0]);
        Assert.Equal("25", lines[1]);
        Assert.Equal("27.5", lines[2]);

        // avg was renamed, and says so.
        Assert.Equal("average", RetiredMembers.Method("query", "avg").Rename);
        Assert.Equal("average", RetiredMembers.Method("model", "avg").Rename);
        Assert.Contains("->average(column)", Fails(Blog + "$ada->posts()->avg('views')").Message);
        Assert.Contains("no SQL form", Fails(Blog + "User->where(fn(u) => $u.posts->avg(fn(p) => $p.views) > 25)->get()").Message);
        // A relation in a condition has the methods a list has: no second names.
        Assert.Contains("no SQL form", Fails(Blog + "User->where(fn(u) => $u.posts->none())->get()").Message);
        Assert.Contains("no SQL form", Fails(Blog + "User->where(fn(u) => $u.posts->length() > 1)->get()").Message);
    }

    [Fact]
    public void AFieldIsSetThroughABareName()
    {
        string[] lines = Lines(Blog + """
            notes.title = 'Notes, again'
            notes.views += 1
            echo [$notes.title, $notes.views]
            totals = { all: [1, 2], by: { ada: 1 } }
            totals.all[0] += 4
            totals.by.ada = 3
            echo [$totals.all[0], $totals.by.ada]
            """);
        Assert.Equal("Notes, again, 6", lines[0]);
        Assert.Equal("5, 3", lines[1]);
    }

    [Fact]
    public void ReadingARelationInALoopLoadsItForTheWholeList()
    {
        string output = Run(Blog + """
            posts = Post->order_by(id)->get()
            @log_sql()
            for p in $posts {
              echo "{$p.title} by {$p.author.name}, {$p.tags->count()} tags"
            }
            """.Replace("@log_sql()", string.Empty), "app.kscript");
        Assert.Contains("Engines by Ada, 2 tags", output);
        Assert.Contains("Hello by Ben, 0 tags", output);

        Database.Log = new List<SqlStatement>();
        Run(Blog + """
            posts = Post->get()
            echo 'start'
            for p in $posts {
              echo $p.author.name
              echo $p.tags->count()
            }
            """);
        int start = Database.Log.FindLastIndex(s => s.Text.StartsWith("SELECT * FROM ", StringComparison.Ordinal) && s.Text.Contains("posts") && !s.Text.Contains(" IN ("));
        // One query for the authors and two for the tags (the pivot, then the tags), not one per post.
        Assert.Equal(3, Database.Log.Count - start - 1);
    }

    [Fact]
    public void WithLoadsRelationsAndTheirRelations()
    {
        Database.Log = new List<SqlStatement>();
        string[] lines = Lines(Blog + """
            users = User->with(profile, posts: fn(q) => $q->where(published: true))->with(posts.tags, 'posts.comments')->order_by(name)->get()
            for u in $users {
              echo "{$u.name}: {$u.posts->map(fn(p) => $p.title + ' ' + $p.tags->count() + ' ' + $p.comments->count())}"
            }
            echo $users[0].profile.bio
            post = Post->with(author)->find($engines.id)
            echo $post->to_json()->contains('"author"')
            echo Post->find($engines.id)->to_json()->contains('"author"')
            $notes->load(tags)
            echo $notes.tags->count()
            echo User->with(posts)->where(name: 'nobody')->get()->count()
            """);
        Assert.Equal("Ada: Notes 1 1", lines[0]);
        Assert.Equal("Ben: Hello 0 0", lines[1]);
        Assert.Equal("Cy:", lines[2]);
        Assert.Equal("Maths", lines[3]);
        Assert.Equal("true", lines[4]);
        Assert.Equal("false", lines[5]);
        Assert.Equal("1", lines[6]);
        Assert.Equal("0", lines[7]);
    }

    [Fact]
    public void WithCountCountsAListRelation()
    {
        string[] lines = Lines(Blog + """
            for u in User->with_count(posts, drafts: fn(q) => $q->where(fn(p) => $p.views > 10))->order_by(name)->get() {
              echo "{$u.name} {$u.posts_count} {$u.drafts_count}"
            }
            echo User->with_count(comments)->find($ada.id).comments_count
            echo User->with_count(posts)->limit(2)->count()
            echo Tag->with_count(posts)->order_by(name)->pluck(name)
            """);
        Assert.Equal("Ada 2 1", lines[0]);
        Assert.Equal("Ben 1 0", lines[1]);
        Assert.Equal("Cy 0 0", lines[2]);
        Assert.Equal("3", lines[3]);
        Assert.Equal("2", lines[4]);
        Assert.Equal("history, maths", lines[5]);
    }

    [Fact]
    public void QueryLambdasReadRelations()
    {
        string[] lines = Lines(Blog + """
            echo User->where(fn(u) => $u.posts->count() > 1)->pluck(name)
            echo User->where(fn(u) => $u.posts->count(fn(p) => $p.published) == 1)->order_by(name)->pluck(name)
            echo User->where(fn(u) => $u.posts->any())->order_by(name)->pluck(name)
            echo User->where(fn(u) => $u.posts->is_empty())->pluck(name)
            echo User->where(fn(u) => not $u.posts->any(fn(p) => $p.published))->order_by(name)->pluck(name)
            echo User->where(fn(u) => $u.posts->any() and $u.posts->all(fn(p) => $p.published))->pluck(name)
            echo User->where(fn(u) => $u.posts->sum(fn(p) => $p.views) > 50)->pluck(name)
            echo User->where(fn(u) => $u.posts->max(fn(p) => $p.views) == 20)->pluck(name)
            echo User->where(fn(u) => $u.posts->any(fn(p) => $p.tags->any(fn(t) => $t.name == 'maths')))->pluck(name)
            echo User->where(fn(u) => $u.comments->count() == 3)->pluck(name)
            echo User->where(fn(u) => $u.drafts->any())->pluck(name)
            echo Post->where(fn(p) => $p.author.name == 'Ben')->pluck(title)
            echo Post->where(fn(p) => $p.author.email == 'BEN@example.com')->pluck(title)
            echo Post->where(fn(p) => $p.author.name->starts_with('A'))->order_by(title)->pluck(title)
            echo Post->where(fn(p) => $p.author.profile.bio == 'Maths')->order_by(title)->pluck(title)
            echo Post->where(fn(p) => $p.author == $ben)->pluck(title)
            echo Post->where(fn(p) => $ben != $p.author)->order_by(title)->pluck(title)
            echo Post->where(fn(p) => $p.editor == null)->order_by(title)->pluck(title)
            echo Post->where(fn(p) => $p.editor != null)->pluck(title)
            echo Post->where(fn(p) => $p.author_id in [$ben])->pluck(title)
            echo User->where(fn(u) => $u.profile == null)->order_by(name)->pluck(name)
            echo User->where(fn(u) => $u.profile != null)->pluck(name)
            echo Comment->where(fn(c) => $c.post.author.name == 'Ada' and $c.body->count() > 2)->order_by(body)->pluck(body)
            echo Post->where(fn(p) => $p.tags->any(fn(t) => $t.posts->count() > 1 and $p.views > 40))->pluck(title)
            """);
        Assert.Equal("Ada", lines[0]);
        Assert.Equal("Ada, Ben", lines[1]);
        Assert.Equal("Ada, Ben", lines[2]);
        Assert.Equal("Cy", lines[3]);
        Assert.Equal("Cy", lines[4]);
        Assert.Equal("Ben", lines[5]);
        Assert.Equal("Ada", lines[6]);
        Assert.Equal("Ben", lines[7]);
        Assert.Equal("Ada", lines[8]);
        Assert.Equal("Ada", lines[9]);
        Assert.Equal("Ada", lines[10]);
        Assert.Equal("Hello", lines[11]);
        Assert.Equal("Hello", lines[12]);
        Assert.Equal("Engines, Notes", lines[13]);
        Assert.Equal("Engines, Notes", lines[14]);
        Assert.Equal("Hello", lines[15]);
        Assert.Equal("Engines, Notes", lines[16]);
        Assert.Equal("Engines, Notes", lines[17]);
        Assert.Equal("Hello", lines[18]);
        Assert.Equal("Hello", lines[19]);
        Assert.Equal("Ben, Cy", lines[20]);
        Assert.Equal("Ada", lines[21]);
        Assert.Equal("Long, Nice", lines[22]);
        Assert.Equal("Engines", lines[23]);
    }

    [Fact]
    public void RelationsInConditionsThatCantBeSqlSayWhy()
    {
        Assert.Contains("is a list of Post records", Fails(Blog + "User->where(fn(u) => $u.posts.title == 'x')->get()").Message);
        Assert.Contains("is a list of Post records", Fails(Blog + "User->where(fn(u) => $u.posts)->get()").Message);
        Assert.Contains("is a Profile record", Fails(Blog + "User->where(fn(u) => $u.profile)->get()").Message);
        Assert.Contains("never null", Fails(Blog + "User->where(fn(u) => $u.posts == null)->get()").Message);
        Assert.Contains("compare one of its fields", Fails(Blog + "User->where(fn(u) => $u.profile == $ada)->get()").Message);
        Assert.Contains("is one User record", Fails(Blog + "Post->where(fn(p) => $p.author->count() > 1)->get()").Message);
        Assert.Contains("takes a lambda", Fails(Blog + "User->where(fn(u) => $u.posts->any(true))->get()").Message);
        Assert.Contains("one parameter", Fails(Blog + "User->where(fn(u) => $u.posts->any(fn(a, b) => true))->get()").Message);
        Assert.Contains("takes no arguments", Fails(Blog + "User->where(fn(u) => $u.posts->is_empty(fn(p) => true))->get()").Message);
        Assert.Contains("needs a lambda", Fails(Blog + "User->where(fn(u) => $u.posts->all())->get()").Message);
        Assert.Contains("picks the value", Fails(Blog + "User->where(fn(u) => $u.posts->sum() > 1)->get()").Message);
        Assert.Contains("has no SQL form", Fails(Blog + "User->where(fn(u) => $u.posts->first() == null)->get()").Message);
        Assert.Contains("hasn't been saved", Fails(Blog + "Post->where(fn(p) => $p.author == User(name: 'x', email: 'x@example.com'))->get()").Message);
    }

    [Fact]
    public void NamedConditionsAndSortingUseTheLinkColumn()
    {
        string[] lines = Lines(Blog + """
            echo Post->where(author: $ben)->pluck(title)
            echo Post->where(author: [$ben, $ada.id])->count()
            echo Post->where(editor: null)->count()
            echo Post->order_by(author: desc)->order_by(title)->pluck(title)
            """);
        Assert.Equal("Hello", lines[0]);
        Assert.Equal("3", lines[1]);
        Assert.Equal("2", lines[2]);
        Assert.Equal("Hello, Engines, Notes", lines[3]);
        Assert.Contains("isn't a column", Fails(Blog + "User->where(posts: 1)->get()").Message);
    }

    [Fact]
    public void ManyToManyLinksAreChanged()
    {
        string[] lines = Lines(Blog + """
            echo $engines->tags()->attach([$maths])
            echo $notes->tags()->sync([$maths.id])
            echo $notes.tags->map(fn(t) => $t.name)
            echo $notes->tags()->toggle([$maths, $history])
            echo $notes.tags->map(fn(t) => $t.name)
            echo $engines->tags()->detach($maths)
            echo $engines.tags->map(fn(t) => $t.name)
            echo $engines->tags()->detach()
            echo $engines.tags->count()
            science = $notes->tags()->create(name: 'science')
            echo $notes.tags->map(fn(t) => $t.name)
            echo $science.posts->map(fn(p) => $p.title)
            echo $engines->tags()->attach(['2'])
            echo $engines->tags()->count()
            """);
        Assert.Equal("0", lines[0]);
        Assert.Equal("{", lines[1]);
        Assert.Contains("history, science", lines);
        Assert.Contains("Notes", lines);
        Assert.Equal("1", lines[^2]);
        Assert.Equal("1", lines[^1]);
        string all = string.Join("\n", lines);
        Assert.Contains("\"attached\": [1]", all.Replace("\n    ", " ").Replace("[ ", "["));
    }

    [Fact]
    public void LinkingNeedsSavedRecordsOfTheRightModel()
    {
        Assert.Contains("hasn't been saved", Fails(Blog + "Post->create(title: 'x', author: User(name: 'x', email: 'x@example.com'))").Message);
        Assert.Contains("can't take a User", Fails(Blog + "$engines->tags()->attach($ada)").Message);
        Assert.Contains("hasn't been saved", Fails(Blog + "$engines->tags()->attach(Tag(name: 'x'))").Message);
        Assert.Contains("nothing to link to", Fails(Blog + "Post(title: 'x', author_id: 1)->tags()->attach(1)").Message);
        Assert.Contains("nothing to link to", Fails(Blog + "User(name: 'x', email: 'x@example.com')->posts()->create(title: 'x')").Message);
        Assert.Contains("pivot table", Fails(Blog + "$ada->posts()->attach(1)").Message);
        Assert.Contains("Set the Post's field instead", Fails(Blog + "$ada->posts()->sync([1])").Message);
        Assert.Contains("pivot table", Fails(Blog + "$engines->author()->detach()").Message);
        Assert.Contains("can't add to it", Fails(Blog + "$ada->comments()->create(body: 'x')").Message);
        Assert.Contains("can't add to it", Fails(Blog + "$engines->author()->create(name: 'x')").Message);
        Assert.Contains("relation of one record", Fails(Blog + "Post->where(published: true)->attach(1)").Message);
        Assert.Contains("takes no arguments", Fails(Blog + "$ada->posts(1)").Message);
        Assert.Contains("needs its author", Fails(Blog + "Post->create(title: 'x')").Message);
        Assert.Contains("isn't saved with the User", Fails(Blog + "User->create(name: 'x', email: 'x@example.com', posts: [$engines])").Message);
        Assert.Contains("can't be set on the record", Fails(Blog + "ada.posts = []").Message);
    }

    [Fact]
    public void SettingALinkSetsItsColumn()
    {
        string[] lines = Lines(Blog + """
            notes.author = $ben
            echo $notes.author_id
            $notes->save()
            echo Post->find($notes.id).author.name
            hello.editor = null
            echo $hello.editor_id ?? 'null'
            $hello->save()
            echo $engines.author.name
            engines.author_id = $ben.id
            echo $engines.author.name
            engines.author_id = $ben.id
            echo $engines.author.name
            draft = Post(title: 'Draft', author: $cy)
            $draft->save()
            echo $draft.author_id
            echo Post->find($draft.id).author.name
            """);
        Assert.Equal(new[] { "2", "Ben", "null", "Ada", "Ben", "Ben", "3", "Cy" }, lines);
    }

    [Fact]
    public void DeletingFollowsTheForeignKeys()
    {
        string[] lines = Lines(Blog + """
            $hello->force_delete()
            echo Post->with_trashed()->count()
            $ben->delete()
            echo Post->with_trashed()->count()
            $ada->delete()
            echo [Post->with_trashed()->count(), Profile->count(), Comment->count(), @db(post_tag)->count()]
            """);
        Assert.Equal("2", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("0, 0, 0, 0", lines[2]);
    }

    [Fact]
    public void SoftDeletedRecordsAreLeftOutOfRelations()
    {
        string[] lines = Lines(Blog + """
            $notes->delete()
            echo $ada.posts->count()
            echo $history.posts->count()
            echo $ada.comments->count()
            echo User->with_count(posts)->find($ada.id).posts_count
            echo User->where(fn(u) => $u.posts->count() == 1)->pluck(name)
            echo Comment->with(post)->where(body: 'Ok')->first().post ?? 'gone'
            """);
        Assert.Equal(new[] { "1", "1", "2", "1", "Ada, Ben", "gone" }, lines);
    }

    [Fact]
    public void ScopesWorkOnModelsQueriesAndRelations()
    {
        string[] lines = Lines(Blog + """
            echo Post->drafts()->pluck(title)
            echo Post->popular()->order_by(title)->pluck(title)
            echo Post->where(published: true)->popular(over: 10)->pluck(title)
            echo $ada->posts()->popular(40)->pluck(title)
            """);
        Assert.Equal(new[] { "Engines", "Engines, Hello", "Hello", "Engines" }, lines);
        Assert.Contains("must return the query", Fails("model A {\n  name: string\n  fn bad(query) => 1\n}\n$db->create_table(A)\nA->bad()").Message);
        Assert.Contains("must return the query", Fails(Blog + "User->with(posts: fn(q) => 1)->get()").Message);
        Assert.Contains("must return the query", Fails(Blog + "User->with_count(posts: fn(q) => 1)->get()").Message);
        Assert.Contains("takes a function", Fails(Blog + "User->with(posts: 1)").Message);
        Assert.Contains("needs a relation", Fails(Blog + "User->with()").Message);
    }

    [Fact]
    public void AScopeOnARelationIsMovedIntoItsSubquery()
    {
        string[] lines = Lines(Blog + """
            model Shelf {
              name: string
              picks: Book[](by: shelf, scope: picked)
            }
            model Book {
              shelf: Shelf
              title: string
              score: int = 0
              notes: Note[]

              fn picked(query) => $query->where(fn(b) => not $b.title->lower()->matches(/^x/) and $b.score in [1, 2, 3] and ($b.score > 1 ? 1 : 0) == 1 and $b.notes->any())
            }
            model Note {
              book: Book
              text: string
            }
            $db->create_table(Shelf)
            $db->create_table(Book)
            $db->create_table(Note)
            s = Shelf->create(name: 'S')
            a = $s->picks()->create(title: 'Alpha', score: 2)
            $s->picks()->create(title: 'Xeno', score: 2)
            $s->picks()->create(title: 'Beta', score: 1)
            $a->notes()->create(text: 'n')
            echo Shelf->with_count(picks)->first().picks_count
            echo Shelf->where(fn(x) => $x.picks->count() == 1)->count()
            echo $s.picks->map(fn(b) => $b.title)
            echo $engines->tags()->attach([null, $maths])
            """);
        Assert.Equal(new[] { "1", "1", "Alpha", "0" }, lines);
    }

    [Fact]
    public void SelfReferencesAndPivotOptions()
    {
        string[] lines = Lines("""
            model Category {
              name: string
              parent?: Category
              children: Category[]
            }
            model Person {
              name: string
              friends: Person[]
              groups: Group[](pivot: memberships)
            }
            model Group {
              name: string
            }
            $db->create_table(Category)
            $db->create_table(Group)
            $db->create_table(Person)
            echo $db->tables()
            top = Category->create(name: 'Top')
            $top->children()->create(name: 'A')
            Category->create(name: 'B', parent: $top)
            echo $top.children->map(fn(c) => $c.name)
            echo Category->where(fn(c) => $c.parent.name == 'Top')->count()
            echo Category->where(fn(c) => $c.children->any())->pluck(name)
            a = Person->create(name: 'A')
            b = Person->create(name: 'B')
            $a->friends()->attach($b)
            echo $a.friends->map(fn(f) => $f.name)
            echo Person->where(fn(p) => $p.friends->any(fn(f) => $f.name == 'B'))->pluck(name)
            $a->groups()->create(name: 'G')
            echo $a.groups->count()
            """);
        Assert.Equal(new[] { "categories", "groups", "memberships", "people", "person_friends" }, lines[0].Split(", ").Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "A, B", "2", "Top", "B", "A", "1" }, lines[1..]);
    }

    [Fact]
    public void RelationsThatCantBeWorkedOutSayWhy()
    {
        Assert.Contains("point at each other", Fails("model A {\n  b: B\n}\nmodel B {\n  a: A\n}\n$db->create_table(A)").Message);
        Assert.Contains("point at each other", Fails("model A {\n  b?: B\n}\nmodel B {\n  a?: A\n}\n$db->create_table(A)").Message);
        Assert.Contains("Say which one links them", Fails("model U {\n  name: string\n  posts: P[]\n}\nmodel P {\n  author: U\n  editor: U\n}\n$db->create_table(U)").Message);
        Assert.Contains("has no field nope", Fails("model U {\n  posts: P[](by: nope)\n}\nmodel P {\n  author: U\n}\n$db->create_table(U)").Message);
        Assert.Contains("no scope by that name", Fails("model U {\n  posts: P[](scope: nope)\n}\nmodel P {\n  author: U\n}\n$db->create_table(U)").Message);
        Assert.Contains("isn't an option", Fails("model U {\n  posts: P[](by: author, max: 3)\n}\nmodel P {\n  author: U\n}\n$db->create_table(U)").Message);
        Assert.Contains("cascade, restrict or set_null", Fails("model P {\n  author: U(on_delete: maybe)\n}\nmodel U {\n  name: string\n}\n$db->create_table(P)").Message);
        Assert.Contains("on_delete is for a field that is one record", Fails("model U {\n  posts: P[](on_delete: cascade)\n}\nmodel P {\n  author: U\n}\n$db->create_table(U)").Message);
        Assert.Contains("can't be a table name", Fails("model U {\n  tags: T[](pivot: 'bad name')\n}\nmodel T {\n  name: string\n}\n$db->create_table(U)").Message);
        Assert.Contains("no relation by that name", Fails("model U {\n  name: string\n  cs: C[](through: nope)\n}\nmodel C {\n  name: string\n}\nU->with_count(cs)").Message);
        Assert.Contains("through works with a has-one or has-many", Fails("model U {\n  name: string\n  tags: T[]\n  cs: C[](through: tags)\n}\nmodel T {\n  name: string\n}\nmodel C {\n  name: string\n}\nU->with_count(cs)").Message);
        Assert.Contains("needs a field that lists C records", Fails("model U {\n  name: string\n  ps: P[]\n  cs: C[](through: ps)\n}\nmodel P {\n  u: U\n}\nmodel C {\n  name: string\n}\nU->with_count(cs)").Message);
        Assert.Contains("more than one", Fails("model U {\n  name: string\n  ps: P[]\n  cs: C[](through: ps)\n}\nmodel P {\n  u: U\n  a: C[](by: p1)\n  b: C[](by: p2)\n}\nmodel C {\n  p1: P\n  p2: P\n  name: string\n}\nU->with_count(cs)").Message);
        Assert.Contains("no relation 'nope'", Fails(Blog + "User->with(nope)->get()").Message);
        Assert.Contains("A relation is a field", Fails("model A {\n  name: string\n}\n$db->create_table(A)\nA->with(x)").Message);
        Assert.Contains("Its relations are", Fails(Blog + "Tag->with(x)").Message);
        Assert.Contains("which has no model", Fails(Blog + "@db(users)->with(posts)").Message);
        Assert.Contains("which has no model", Fails(Blog + "@db(users)->with_count(posts)").Message);
        Assert.Contains("is one User", Fails(Blog + "Post->with_count(author)").Message);
    }

    [Fact]
    public void AModelWithAnotherKeyLinksByIt()
    {
        string[] lines = Lines("""
            model Country(key: code) {
              code: string(max: 2)
              name: string
              cities: City[]
            }
            model City {
              name: string
              country: Country
            }
            $db->create_table(Country)
            $db->create_table(City)
            nz = Country->create(code: 'nz', name: 'New Zealand')
            $nz->cities()->create(name: 'Wellington')
            echo City->first().country_code
            echo City->first().country.name
            echo Country->with_count(cities)->first().cities_count
            """);
        Assert.Equal(new[] { "nz", "New Zealand", "1" }, lines);
    }

    [Fact]
    public void ARelationDescribesItself()
    {
        Run(Blog);
        var types = new Dictionary<string, ModelInfo>();
        foreach (string name in new[] { "User", "Post", "Tag" })
        {
            types[name] = ModelInfo.OfTable(ModelInfo.TableName(name));
        }
        Assert.Equal("belongs to User (posts.author_id)", types["Post"].Relation("author").Describe());
        Assert.Equal("has one Profile (profiles.user_id)", types["User"].Relation("profile").Describe());
        Assert.Equal("has many Post (posts.author_id)", types["User"].Relation("posts").Describe());
        Assert.Equal("many to many with Post (pivot table post_tag)", types["Tag"].Relation("posts").Describe());
        Assert.Equal("has many Comment through posts", types["User"].Relation("comments").Describe());
        Assert.Equal("y", ModelInfo.Singular("ys"));
        Assert.Equal("box", ModelInfo.Singular("boxes"));
        Assert.Equal("person", ModelInfo.Singular("people"));
        Assert.Equal("glass", ModelInfo.Singular("glass"));
        Assert.Equal("category", ModelInfo.Singular("categories"));
        Assert.Equal(60, Dialect.ForeignKeyName(new string('a', 40), new string('b', 30)).Length);
    }

    [Fact]
    public void ModelsInTheModelsFolderNameEachOtherWithoutImports()
    {
        FileOf("kscript.json", "{ \"name\": \"blog\" }");
        FileOf("models/user.kscript", "import KScript.Data\n\nmodel User {\n  name: string\n  posts: Post[]\n}\n");
        FileOf("models/post.kscript", "import KScript.Data\n\nmodel Post {\n  title: string\n  author: User\n}\n");
        string[] lines = Lines($$"""
            import "models/post.kscript"

            $db->sql('create table users (id {{AutoId}}, name text)')
            $db->create_table(Post)
            $db->sql("insert into users (name) values ('Ada')")
            Post->create(title: 'Engines', author_id: 1)
            post = Post->first()
            echo $post.author.name
            echo $post.author->posts()->count()
            """);
        Assert.Equal(new[] { "Ada", "1" }, lines);
    }
}
