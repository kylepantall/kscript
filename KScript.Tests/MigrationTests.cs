using System.Globalization;

using KScript.Data;
using KScript.Data.Migrations;
using KScript.Databases;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>kscript db: migrations written from the models, run, undone and checked, on SQLite or the test server.</summary>
public sealed class MigrationTests : DataFixture
{
    private int minute;

    public MigrationTests()
    {
        // Each new migration is a minute after the last, so they sort in the order they were made.
        KScript.Scaffolding.Scaffolder.Clock = () => new DateTime(2026, 11, 22, 10, 0, 0).AddMinutes(minute++);
        FileOf("kscript.json", "{ \"name\": \"blog\" }");
        if (Server != null) DatabaseServer.Reset(Server);
    }

    private (int Code, string Out, string Error) Db(params string[] args) => DbWith(null, args);

    private (int Code, string Out, string Error) DbWith(string answers, params string[] args)
    {
        Database.CloseAll();
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var error = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        int code = DbCommand.Run(args.Concat(new[] { "--no-ansi" }).ToArray(), output, error, Project, input: answers == null ? null : new StringReader(answers), interactive: answers != null);
        return (code, output.ToString(), error.ToString());
    }

    private string Ok(params string[] args)
    {
        var (code, output, error) = Db(args);
        Assert.True(code == 0, $"kscript db {string.Join(" ", args)} failed ({code}):\n{error}\n{output}");
        return output;
    }

    private string Failed(params string[] args)
    {
        var (code, output, error) = Db(args);
        Assert.True(code != 0, $"kscript db {string.Join(" ", args)} should have failed:\n{output}");
        return error + output;
    }

    private string[] Migrations() =>
        Directory.Exists(Path.Combine(Project, "database", "migrations"))
            ? Directory.GetFiles(Path.Combine(Project, "database", "migrations")).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

    /// <summary>A migration's text, with calls kscript fmt wrapped put back on one line.</summary>
    private string Migration(string name)
    {
        string text = File.ReadAllText(Path.Combine(Project, "database", "migrations", Migrations().Single(f => f.Contains(name, StringComparison.Ordinal))));
        return System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(text, @"\(\n\s+", "("), @",?\n\s*\)", ")").Replace(",\n    ", ", ");
    }

    // Raw strings have the file's line ends (\r\n in a Windows checkout); the tests edit them by \n.
    private static readonly string User = """
        import KScript.Data

        enum Role { member, admin }

        model User(timestamps, unique: [email]) {
          name: string(max: 80)
          email: email
          role: Role = 'member'
          active: bool = true
          posts: Post[]
        }
        """.ReplaceLineEndings("\n");

    private static readonly string Post = """
        import KScript.Data

        model Post {
          title: string
          views: int = 0
          author: User
          tags: Tag[]
        }
        """.ReplaceLineEndings("\n");

    private static readonly string Tag = """
        import KScript.Data

        model Tag {
          name: string
          posts: Post[]
        }
        """.ReplaceLineEndings("\n");

    /// <summary>Stands for the Tag model in <see cref="Models"/>; null there means no Tag model.</summary>
    private const string DefaultTag = "\0tag";

    private void Models(string user = null, string post = null, string tag = DefaultTag)
    {
        if (tag == DefaultTag) tag = Tag;
        FileOf("models/user.kscript", user ?? User);
        FileOf("models/post.kscript", post ?? Post);
        if (tag != null) FileOf("models/tag.kscript", tag);
        else File.Delete(Path.Combine(Project, "models", "tag.kscript"));
    }

    /// <summary>Runs code without emptying the database first.</summary>
    private string Keep(string code)
    {
        Database.CloseAll();
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        string imports = string.Concat(new[] { "user", "post", "tag" }.Where(m => File.Exists(Path.Combine(Project, "models", m + ".kscript"))).Select(m => $"import \"models/{m}.kscript\"\n"));
        FileOf("keep.kscript", "import KScript.Data\n" + imports + code + "\n");
        new Interpreter(KScriptHost.ForText(output)).RunFile(Path.Combine(Project, "keep.kscript"));
        return output.ToString().TrimEnd('\n');
    }

    [Fact]
    public void DiffReadsAModelKeptInsideAComponent()
    {
        FileOf("_components/comment-thread.kscript", """
            import KScript.Web
            import KScript.Data

            type CommentThread extends Component {
              model Comment(timestamps) {
                body: string(max: 2000)
              }

              fn render(): Component {
                return ```html
                <p>{Comment->count()}</p>
                ```
              }
            }
            """.ReplaceLineEndings("\n"));
        // A component with no model of its own is not run to look for one.
        FileOf("_components/plain.kscript", "import KScript.Web\n\ntype Plain extends Component {\n  fn render(): Component {\n    return ```html\n    <p></p>\n    ```\n  }\n}\nthrow 'not run'\n");
        Assert.Contains("create_comments.kscript, with 1 step.", Ok("diff"));
        Assert.Contains("Field(name: 'body', type: string, max: 2000)", Migration("create_comments"));
        Ok("migrate");
        Assert.Contains("Nothing to write", Ok("diff"));
    }

    [Fact]
    public void DiffWritesTheTablesTheModelsWantAndMigrateMakesThem()
    {
        Models();
        string wrote = Ok("diff");
        Assert.Contains("Wrote database/migrations/2026_11_22_100000_create_tags_users_posts_and_more.kscript, with 4 steps.", wrote);
        string text = Migration("create_");
        Assert.Contains("CreateTable(name: 'users') {", text);
        Assert.Contains("Field(name: 'id', type: int, key, auto)", text);
        Assert.Contains("Field(name: 'name', type: string, max: 80)", text);
        Assert.Contains("Field(name: 'email', type: email)", text);
        Assert.Contains("Field(name: 'role', type: enum, values: [member, admin], default: 'member')", text);
        Assert.Contains("Field(name: 'active', type: bool, default: true)", text);
        Assert.Contains("Field(name: 'created_at', type: date, optional)", text);
        Assert.Contains("Index(columns: [email], unique)", text);
        Assert.Contains("Field(name: 'author_id', type: int, references: 'users')", text);
        Assert.Contains("Key(columns: [post_id, tag_id])", text);
        // Tables come after the ones they point to.
        Assert.True(text.IndexOf("name: 'users'", StringComparison.Ordinal) < text.IndexOf("name: 'posts'", StringComparison.Ordinal));
        Assert.True(text.IndexOf("name: 'tags'", StringComparison.Ordinal) < text.IndexOf("name: 'post_tag'", StringComparison.Ordinal));

        string status = Ok("status");
        Assert.Contains("2026_11_22_100000_create_tags_users_posts_and_more", status);
        Assert.Contains("Pending", status);

        string ran = Ok("migrate");
        Assert.Contains("Running migrations.", ran);
        Assert.Contains("2026_11_22_100000_create_tags_users_posts_and_more", ran);
        Assert.Contains("DONE", ran);
        Assert.Contains("Ran (batch 1)", Ok("status"));
        Assert.Contains("Nothing to migrate", Ok("migrate"));
        Assert.Contains("Nothing to write", Ok("diff"));

        // The tables work: defaults from the database, foreign keys, the pivot.
        Assert.Equal("member true 0 1", Keep("""
            ada = User->create(name: 'Ada', email: 'ada@example.com')
            post = $ada->posts()->create(title: 'Engines')
            tag = Tag->create(name: 'maths')
            $post->tags()->attach($tag)
            $db->sql("insert into users (name, email) values ('Ben', 'ben@example.com')")
            ben = User->where(name: 'Ben')->first()
            echo "{$ben.role} {$ben.active} {$post->fresh().views} {$tag->posts()->count()}"
            """));
    }

    [Fact]
    public void ChangedModelsGiveAMigrationOfTheChangesAndRollbackUndoesIt()
    {
        Models();
        Ok("diff");
        Ok("migrate");
        Keep("User->create(name: 'Ada', email: 'ada@example.com')");

        Models(user: User.Replace("  name: string(max: 80)\n", "  name: string(max: 120)\n  bio?: string\n  score: number = 1.5\n")
            .Replace("enum Role { member, admin }", "enum Role { member, editor, admin }")
            .Replace("unique: [email])", "unique: [email], index: [[name, active]])"),
            post: Post.Replace("  views: int = 0\n", string.Empty));
        string status = Ok("status");
        Assert.Contains("The models want 6 changes", status);
        Ok("diff", "grow_users");
        string text = Migration("grow_users");
        Assert.Contains("AddField(table: 'users', name: 'bio', type: string, optional)", text);
        Assert.Contains("AddField(table: 'users', name: 'score', type: number, default: 1.5)", text);
        Assert.Contains("ChangeField(table: 'users', name: 'name', type: string, max: 120)", text);
        Assert.True(text.Contains("ChangeField(table: 'users', name: 'role', type: enum, values: [member, editor, admin], default: 'member')", StringComparison.Ordinal), text);
        Assert.Contains("DropField(table: 'posts', name: 'views')", text);
        Assert.Contains("AddIndex(table: 'users', columns: [name, active])", text);

        Ok("migrate");
        Assert.Contains("Ran (batch 2)", Ok("status"));
        Assert.Equal("Ada 1.5 editor", Keep("""
            ada = User->first()
            ada.role = 'editor'
            $ada->save()
            echo "{$ada.name} {$ada.score} {User->first().role}"
            """));

        // Rollback undoes the last batch: the rows stay, in the old shape.
        Keep("$db->sql(\"update users set role = 'admin'\")");
        string undone = Ok("rollback");
        Assert.Contains("Rolling back migrations.", undone);
        Assert.Contains("grow_users", undone);
        Assert.Contains("Pending", Ok("status"));
        Models();
        Assert.Equal("Ada admin 0", Keep("""
            ada = User->first()
            echo "{$ada.name} {$ada.role} {Post->count()}"
            """));
        // The rolled back migration is still there, so the old models want it undone.
        Assert.Contains("drop field users.bio", Ok("status"));
        Ok("migrate");
        Ok("rollback", "--all");
        Assert.Contains("Nothing to roll back", Ok("rollback"));
    }

    [Fact]
    public void RenamesAreAskedAbout()
    {
        Models();
        Ok("diff");
        Ok("migrate");
        Keep("User->create(name: 'Ada', email: 'ada@example.com')");
        Models(user: User.Replace("  name: string(max: 80)", "  full_name: string(max: 80)"),
            post: Post.Replace("model Post {", "model Post(table: 'articles') {"));

        // Asked: yes to both renames.
        var (code, output, error) = DbWith("y\ny\n", "diff", "renames");
        Assert.True(code == 0, error);
        Assert.Contains("Was the table posts renamed to articles?", output);
        Assert.Contains("Was users.name renamed to full_name?", output);
        string text = Migration("renames");
        Assert.Contains("RenameTable(from: 'posts', to: 'articles')", text);
        Assert.Contains("RenameField(table: 'users', from: 'name', to: 'full_name')", text);
        Assert.DoesNotContain("DropField", text);
        Ok("migrate");
        Assert.Equal("Ada", Keep("echo User->first().full_name"));
        Assert.Contains("Nothing to write", Ok("diff"));
        Ok("rollback");
        File.Delete(Path.Combine(Project, "database", "migrations", Migrations().Single(f => f.Contains("renames", StringComparison.Ordinal))));

        // Not asked (no one to ask): the old ones are dropped and the new ones made.
        string unasked = Ok("diff", "replace");
        Assert.Contains("If you renamed the table posts to articles, the migration drops it", unasked);
        Assert.Contains("If you renamed users.name to full_name", unasked);
        text = Migration("replace");
        Assert.Contains("DropField(table: 'users', name: 'name')", text);
        Assert.Contains("AddField(table: 'users', name: 'full_name', type: string, max: 80)", text);
        Assert.Contains("CreateTable(name: 'articles')", text);
        Assert.Contains("DropTable(name: 'posts')", text);
        // Asked, and told no.
        File.Delete(Path.Combine(Project, "database", "migrations", Migrations().Single(f => f.Contains("replace", StringComparison.Ordinal))));
        (code, output, _) = DbWith("n\nn\n", "diff", "replace");
        Assert.Equal(0, code);
        Assert.DoesNotContain("If you renamed", output);
        Assert.Contains("DropTable(name: 'posts')", Migration("replace"));
    }

    [Fact]
    public void HandWrittenStepsRunAndAreUndone()
    {
        FileOf("database/migrations/2026_01_01_000000_teams.kscript", """
            import KScript.Data

            Migration {
              CreateTable(name: 'teams') {
                Field(name: 'id', type: int, key, auto)
                Field(name: 'name', type: string, max: 40)
                Field(name: 'level', type: enum, values: [low, high], optional)
              }
              CreateTable(name: 'people') {
                Field(name: 'code', type: string, max: 10, key)
                Field(name: 'team_id', type: int, references: 'teams', optional)
                Field(name: 'nick', type: string, optional)
                Field(name: 'joined', type: date, optional)
                Field(name: 'email', type: email, optional)
                Field(name: 'extra', type: json, optional)
                Field(name: 'secret', type: password, optional)
                Field(name: 'tag', type: handle, optional)
                Field(name: 'ref', type: uuid, optional)
                Index(columns: [nick])
              }
              CreateTable(name: 'memberships') {
                Field(name: 'person_code', type: string, max: 10, references: 'people', references_column: 'code')
                Field(name: 'team_id', type: int, references: 'teams', on_delete: restrict)
                Key(columns: [person_code, team_id])
              }
              Sql(up: "INSERT INTO teams (name, level) VALUES ('Red', 'low')", down: "DELETE FROM teams")
            }
            """);
        FileOf("database/migrations/2026_01_02_000000_change.kscript", """
            import KScript.Data

            Migration {
              AddField(table: 'teams', name: 'size', type: int, default: 3)
              AddField(table: 'teams', name: 'owner_code', type: string, max: 10, references: 'people', optional)
              RenameField(table: 'people', from: 'nick', to: 'handle')
              ChangeField(table: 'teams', name: 'level', type: enum, values: [low, mid, high], default: 'mid')
              ChangeField(table: 'teams', name: 'name', type: string, max: 80, optional)
              AddIndex(table: 'teams', columns: [name, size], unique)
              DropIndex(table: 'people', columns: [handle])
              RenameTable(from: 'memberships', to: 'members')
              DropField(table: 'people', name: 'joined')
            }
            """);
        string ran = Ok("migrate");
        Assert.Contains("2026_01_01_000000_teams", ran);
        Assert.Contains("2026_01_02_000000_change", ran);
        Assert.Equal("Red low 3 1", Keep("""
            t = $db->sql('select * from teams')[0]
            echo "{$t.name} {$t.level} {$t.size} {$db->sql('select count(*) as n from teams')[0].n}"
            """));
        Assert.Contains("already has that", Keep("""
            try {
              $db->sql("insert into teams (name, size) values ('Red', 3)")
            } catch UniqueViolation as e {
              echo $e.message
            }
            """));
        Assert.Contains("refused", Keep("""
            try {
              $db->sql("insert into teams (name, level) values ('Blue', 'top')")
            } catch Query as e {
              echo $e.message
            }
            """));

        // Rollback --steps 1 undoes the second; --all the first too, with its Sql's down.
        Ok("rollback", "--steps", "1");
        Assert.Equal("low", Keep("echo $db->sql('select level from teams')[0].level"));
        Assert.Contains("Ran (batch 1)", Ok("status"));
        Ok("rollback", "--all");
        Assert.DoesNotContain("teams", Keep("echo $db->tables()"));
        Assert.Contains("Pending", Ok("status"));
    }

    [Fact]
    public void PretendPrintsTheSqlInsteadOfRunningIt()
    {
        Models();
        Assert.Contains("CreateTable(name: 'users')", Ok("diff", "--pretend"));
        Assert.Empty(Migrations());
        Ok("diff");
        string sql = Ok("migrate", "--pretend");
        Assert.Contains("-- 2026_11_22_100100_create_tags_users_posts_and_more", sql);
        Assert.Contains("CREATE TABLE", sql);
        Assert.Contains("Pending", Ok("status"));
        Ok("migrate");
        string undo = Ok("rollback", "--pretend");
        Assert.Contains("-- undo 2026_11_22_100100_create_tags_users_posts_and_more", undo);
        Assert.Contains("DROP TABLE", undo);
        Assert.Contains("Ran (batch 1)", Ok("status"));
    }

    [Fact]
    public void FreshDropsEveryTableAndSeedFillsThem()
    {
        Models();
        Ok("diff");
        FileOf("database/seeders/users.kscript", """
            import KScript.Data
            import "../../models/user.kscript"

            n = User->count()
            User->create(name: 'Ada', email: "ada{ $n }@example.com")
            echo 'seeded users'
            """);
        FileOf("database/seeders/zz-empty.kscript", "echo 'empty seeder'\n");
        string out1 = Ok("migrate", "--seed");
        Assert.Contains("Seeding the database.", out1);
        Assert.Contains("seeded users", out1);
        Assert.Contains("empty seeder", out1);
        Assert.Equal("1", Keep("echo User->count()"));

        string fresh = Ok("fresh", "--seed");
        Assert.Contains("Dropped", fresh);
        Assert.Equal("1", Keep("echo User->count()"));
        Assert.Contains("seeded users", Ok("seed", "users"));
        Assert.Equal("2", Keep("echo User->count()"));
        Assert.Contains("Did you mean users?", Failed("seed", "user5"));
        FileOf("database/seeders/zz-empty.kscript", "exit(3)\n");
        Assert.Equal(3, Db("seed").Code);
        FileOf("database/seeders/zz-empty.kscript", "echo 'a'\nx = 1 / 0\n");
        Assert.Contains("zz-empty.kscript", Failed("seed", "zz-empty"));

        Directory.Delete(Path.Combine(Project, "database", "seeders"), recursive: true);
        Assert.Contains("There are no seeders", Ok("seed"));
        Assert.Contains("Make one with kscript make seeder", Failed("seed", "users"));
        Ok("rollback", "--all");
        Keep("$db->sql('drop table kscript_migrations')");
        Assert.Contains("The database had no tables.", Ok("fresh"));
    }

    [Fact]
    public void ProductionNeedsForce()
    {
        Models();
        Ok("diff");
        FileOf("kscript.json", "{ \"name\": \"blog\", \"config\": { \"environment\": \"production\" } }");
        var (code, output, error) = Db("migrate");
        Assert.Equal(1, code);
        Assert.Contains("the environment is production", error);
        Assert.Contains("db fresh drops every table", Failed("fresh"));
        Assert.Contains("db rollback --force", Failed("rollback"));
        Assert.Contains("db seed --force", Failed("seed"));
        Ok("migrate", "--force");
        var no = DbWith("n\n", "rollback");
        Assert.True(no.Code == 1, no.Out + no.Error);
        Assert.Equal(0, DbWith("y\n", "rollback").Code);
        var ended = DbWith(string.Empty, "rollback");
        Assert.Contains("The input ended before the question was answered", ended.Error);
        FileOf("kscript.json", "{ nope");
        Assert.Equal(2, Db("status").Code);
    }

    [Fact]
    public void MistakesAreExplained()
    {
        Assert.Contains("There are no models in models", Failed("diff"));
        Assert.Contains("There are no migrations", Ok("status"));
        Assert.Contains("Nothing to roll back", Ok("rollback"));
        Assert.Contains("Did you mean db migrate?", Failed("migrat"));
        Assert.Contains("--steps needs a number", Failed("rollback", "--steps", "x"));
        Assert.Equal(2, Db().Code);
        Assert.Contains("Usage: kscript db", Ok("--help"));

        void Bad(string body, string message)
        {
            Database.CloseAll();
            if (Server != null) DatabaseServer.Reset(Server);
            try { Directory.Delete(Path.Combine(Project, "data"), recursive: true); } catch (DirectoryNotFoundException) { }
            try { Directory.Delete(Path.Combine(Project, "database"), recursive: true); } catch (DirectoryNotFoundException) { }
            FileOf("database/migrations/2026_01_01_000000_bad.kscript", "import KScript.Data\n\nMigration {\n" + body + "\n}\n");
            string text = Failed("migrate");
            Assert.True(text.Contains(message, StringComparison.Ordinal), $"Expected '{message}' in:\n{text}");
        }

        const string Teams = "CreateTable(name: 'teams') {\n  Field(name: 'id', type: int, key, auto)\n  Field(name: 'name', type: string, max: 40)\n}\n";
        Bad("DropTable(name: 'team')", "the migrations before this one don't make a table called team");
        Bad(Teams + "DropTable(name: 'team')", "Did you mean teams?");
        Bad(Teams + Teams, "an earlier migration made it already");
        Bad("CreateTable(name: 'x') {\n}", "needs at least one Field");
        Bad("CreateTable(name: 'x') {\n  Field(name: 'a', type: int)\n  Field(name: 'a', type: int)\n}", "two columns called a");
        Bad("CreateTable(name: 'x') {\n  Field(name: 'a', type: int, key)\n  Field(name: 'b', type: int)\n  Key(columns: [a, b])\n}", "more than one key");
        Bad("CreateTable(name: 'x') {\n  Field(name: 'a', type: int)\n  Key(columns: [a, c])\n}", "names c, which isn't one of its columns");
        Bad("CreateTable(name: 'x') {\n  Field(name: 'a', type: int)\n  Index(columns: [b])\n}", "x has no column called b");
        Bad("CreateTable(name: 'x') {\n  Field(name: 'a', type: int, references: 'nope')\n}", "there is no table called nope by then");
        Bad("CreateTable(name: 'x') {\n  Field(name: 'a', type: int)\n}\nCreateTable(name: 'y') {\n  Field(name: 'a', type: int, references: 'x')\n}", "has no key of one column");
        Bad(Teams + "CreateTable(name: 'y') {\n  Field(name: 'a', type: int, references: 'teams', references_column: 'nope')\n}", "teams has no column called nope");
        Bad(Teams + "CreateTable(name: 'y') {\n  Field(name: 't', type: int, references: 'teams')\n}\nDropTable(name: 'teams')", "y.t points to it");
        Bad(Teams + "RenameTable(from: 'teams', to: 'teams')", "there is a table called teams already");
        Bad(Teams + "AddField(table: 'teams', name: 'name', type: string)", "it has that column already");
        Bad(Teams + "AddField(table: 'teams', name: 'k', type: int, key)", "a table's key is made with it");
        Bad(Teams + "DropField(table: 'teams', name: 'id')", "it is the table's key");
        Bad(Teams + "DropField(table: 'teams', name: 'nme')", "Did you mean name?");
        Bad(Teams + "CreateTable(name: 'y') {\n  Field(name: 't', type: int, references: 'teams')\n}\nDropField(table: 'teams', name: 'id')", "is the table's key");
        Bad(Teams + "CreateTable(name: 'y') {\n  Field(name: 't', type: string, references: 'teams', references_column: 'name')\n}", "which isn't the table's key or unique");
        Bad(Teams + "AddIndex(table: 'teams', columns: [name], unique)\nCreateTable(name: 'y') {\n  Field(name: 't', type: string, references: 'teams', references_column: 'name')\n}\nDropField(table: 'teams', name: 'name')", "y.t points to it");
        Bad(Teams + "RenameField(table: 'teams', from: 'name', to: 'id')", "there is a column called id already");
        Bad(Teams + "ChangeField(table: 'teams', name: 'name', type: int, key)", "whether teams.name is the key");
        Bad(Teams + "AddIndex(table: 'teams', columns: [name])\nAddIndex(table: 'teams', columns: [name])", "has that index already");
        Bad(Teams + "DropIndex(table: 'teams', columns: [name], unique)", "has no unique index on (name) to drop");
        Bad("Field(name: 'a', type: int)", "Field");
        Bad("CreateTable(name: 'x') {\n  CreateTable(name: 'y') {\n  }\n}", "can't be inside another");
        Bad(Teams + "AddField(table: 'teams', name: 'a', type: nope)", "type: nope isn't one of: string, int");
        Bad(Teams + "AddField(table: 'teams', name: 'a', type: enum)", "An enum column needs its values");
        Bad(Teams + "AddField(table: 'teams', name: 'a', type: string, max: 0)", "max should be a number");
        Bad(Teams + "AddField(table: 'teams', name: 'a', type: string, default: [1])", "A default is text, a number, true or false");
        Bad(Teams + "AddField(table: 'teams', name: 'a', type: string, auto)", "auto numbers the rows of an int key");
        Bad(Teams + "AddField(table: 'teams', name: 'a', type: int, references: 'teams', on_delete: set_null)", "needs a column that may be empty (optional)");
        Bad("Down {\n}\nDown {\n}", "one Down block");
        Bad(Teams + "Sql(up: \"INSERT INTO nope VALUES (1)\")", "failed at 'SQL: INSERT INTO nope VALUES (1)'");

        // Not a migration, or run on its own.
        try { Directory.Delete(Path.Combine(Project, "database"), recursive: true); } catch (DirectoryNotFoundException) { }
        FileOf("database/migrations/2026_01_01_000000_script.kscript", "echo 1\n");
        Assert.Contains("holds Migration { } after import KScript.Data", Failed("migrate"));
        FileOf("database/migrations/2026_01_01_000000_script.kscript", "import KScript.Data\n\nMigration {\n" + Teams + "}\n");
        FileOf("database/migrations/sub/2026_01_01_000000_script.kscript", "import KScript.Data\n\nMigration {\n}\n");
        Assert.Contains("Two migrations are called 2026_01_01_000000_script", Failed("migrate"));
        Directory.Delete(Path.Combine(Project, "database", "migrations", "sub"), recursive: true);
        var e = Assert.Throws<KScriptException>(() => Run("import \"database/migrations/2026_01_01_000000_script.kscript\""));
        Assert.Contains("runs with kscript db migrate", e.Message);

        // Can't be undone: SQL with no down, or a file that has gone.
        FileOf("database/migrations/2026_01_02_000000_sql.kscript", "import KScript.Data\n\nMigration {\n  Sql(up: \"UPDATE teams SET name = 'x'\")\n}\n");
        Ok("migrate");
        Assert.Contains("nothing says how to undo SQL: UPDATE teams SET name = 'x'", Failed("rollback"));
        File.Delete(Path.Combine(Project, "database", "migrations", "2026_01_02_000000_sql.kscript"));
        Assert.Contains("file missing", Ok("status"));
        Assert.Contains("its file isn't in database/migrations any more", Failed("rollback"));
    }

    [Fact]
    public void ADownBlockIsWhatRollbackRuns()
    {
        FileOf("database/migrations/2026_01_01_000000_down.kscript", """
            import KScript.Data

            Migration {
              CreateTable(name: 'notes') {
                Field(name: 'id', type: int, key, auto)
                Field(name: 'body', type: string)
              }
              Down {
                Sql(up: "DROP TABLE notes")
              }
            }
            """);
        Ok("migrate");
        Assert.Contains("notes", Keep("echo $db->tables()"));
        Ok("rollback");
        Assert.DoesNotContain("notes", Keep("echo $db->tables()"));
    }

    [Fact]
    public void StatusAsJson()
    {
        Models();
        Ok("diff");
        Ok("migrate");
        Models(post: Post.Replace("  views: int = 0\n", "  views: int = 0\n  draft: bool = false\n"));
        string json = Ok("status", "--output", "json");
        Assert.Contains("\"ran\": true", json);
        Assert.Contains("\"batch\": 1", json);
        Assert.Contains("add field posts.draft", json);
    }

    [Fact]
    public void MigrationsCanBeXml()
    {
        Models();
        Ok("diff", "--syntax", "xml");
        Assert.EndsWith(".ks.xml", Migrations().Single());
        string xml = Migration("create");
        Assert.Contains("<KScript.Migration>", xml);
        Assert.Contains("<Field name=\"role\" type=\"enum\" values=\"member, admin\" default=\"'member'\" />", xml);
        Ok("migrate");
        Assert.Contains("Nothing to write", Ok("diff"));
    }

    [Fact]
    public void TheSchemaOfModelsAndTheWriter()
    {
        Assert.Equal("'posts'", MigrationWriter.Name("posts"));
        Assert.Equal("'two words'", MigrationWriter.Name("two words"));
        Assert.Equal("\"it's\"", MigrationWriter.Literal("it's"));
        Assert.Equal("'x'", MigrationWriter.Literal("x"));
        Assert.Equal("2", MigrationWriter.Literal(2.0));
        Assert.Equal("false", MigrationWriter.Literal(false));
        Assert.Equal(ColumnKind.Text, ColumnSpec.KindOf("text"));
        Assert.Equal(ColumnKind.Integer, ColumnSpec.KindOf("integer"));
        Assert.Equal(ColumnKind.Real, ColumnSpec.KindOf("float"));
        Assert.Null(ColumnSpec.KindOf("blob"));
        Assert.Equal("update_schema", Differ.NameFor(new Step[] { new SqlStep("x", null) }));
        Assert.Equal("update_users", Differ.NameFor(new Step[] { new DropTableStep("users") }));
        Assert.Equal("update_users", Differ.NameFor(new Step[] { new RenameTableStep("people", "users"), new AddIndexStep("users", new[] { "a" }, false) }));
        var sql = new SqlStep(new string('x', 50), null);
        Assert.EndsWith("…", sql.Describe());
        Assert.Null(sql.Reverse(new Schema()));
        Assert.Equal("SQL: y", Assert.Single(new SqlStep("x", "y").Reverse(new Schema())).Describe());

        // Every kind of step names the table it changes.
        var teams = new TableSpec { Name = "teams" };
        teams.Columns.Add(new ColumnSpec { Name = "id", Kind = ColumnKind.Integer, IsKey = true });
        teams.Columns.Add(new ColumnSpec { Name = "name", Kind = ColumnKind.Text });
        teams.Indexes.Add(new TableIndex("teams_name_index", new[] { "name" }, false));
        ColumnSpec size = new ColumnSpec { Name = "size", Kind = ColumnKind.Integer, Nullable = true };
        Assert.Equal("update_teams", Differ.NameFor(new Step[]
        {
            new CreateTableStep(teams), new AddColumnStep("teams", size), new DropColumnStep("teams", "size"), new RenameColumnStep("teams", "a", "b"),
            new ChangeColumnStep("teams", size), new DropIndexStep("teams", new[] { "name" }, false),
        }));

        // Dropped tables and indexes come back when undone, and every step can be written.
        var schema = new Schema();
        schema.Add(teams);
        Assert.Equal("create table teams", Assert.Single(new DropTableStep("teams").Reverse(schema)).Describe());
        Assert.IsType<AddIndexStep>(Assert.Single(new DropIndexStep("teams", new[] { "name" }, false).Reverse(schema)));
        string file = MigrationWriter.File(new Step[] { new DropIndexStep("teams", new[] { "name" }, true), new SqlStep("UPDATE teams SET name = 'x'", "SELECT 1") }, null);
        Assert.Contains("DropIndex(table: 'teams', columns: [name], unique)", file);
        Assert.Contains("Sql(up: \"UPDATE teams SET name = 'x'\", down: 'SELECT 1')", file);
        Assert.Contains("Sql(up: 'SELECT 2')", MigrationWriter.File(new Step[] { new SqlStep("SELECT 2", null) }, null));
    }

    [Fact]
    public void EachDatabaseHasItsOwnSql()
    {
        var before = new TableSpec { Name = "orders" };
        before.Columns.Add(new ColumnSpec { Name = "id", Kind = ColumnKind.Integer, IsKey = true });
        before.Columns.Add(new ColumnSpec { Name = "status", Kind = ColumnKind.Enum, Words = new[] { "open", "paid" } });
        before.Columns.Add(new ColumnSpec { Name = "code", Kind = ColumnKind.Text, Length = 20 });
        before.Indexes.Add(new TableIndex("orders_code_index", new[] { "code" }, false));
        ColumnSpec status = new ColumnSpec { Name = "status", Kind = ColumnKind.Enum, Words = new[] { "open", "paid", "sent" } };
        ColumnSpec code = new ColumnSpec { Name = "code", Kind = ColumnKind.Integer };
        ColumnSpec email = new ColumnSpec { Name = "code", Kind = ColumnKind.Email, Length = 20 };

        // Postgres: the check on an enum is looked up when it runs; a new type is cast.
        Ddl pg = Ddl.For(PostgresDialect.Instance);
        string pretend = string.Join("\n", pg.ChangeColumn(before, before, before.Column("status"), status, null));
        Assert.Contains("-- drops the check constraints on orders.status", pretend);
        Assert.Contains("ADD CONSTRAINT \"orders_status_check\" CHECK (\"status\" IN ('open', 'paid', 'sent'))", pretend);
        string cast = string.Join("\n", pg.ChangeColumn(before, before, before.Column("code"), code, null));
        Assert.Contains("ALTER COLUMN \"code\" TYPE BIGINT USING \"code\"::BIGINT", cast);
        Assert.Contains("COLLATE \"kscript_nocase\"", string.Join("\n", pg.ChangeColumn(before, before, before.Column("code"), email, null)));
        Assert.Contains("COLLATE \"default\"", string.Join("\n", pg.ChangeColumn(before, before, email, before.Column("code"), null)));

        // MySQL drops a column's check itself; MariaDB's MODIFY replaces it.
        Ddl my = Ddl.For(MySqlDialect.MySql);
        Assert.Contains("-- drops the check constraints on orders.status (MySQL)", string.Join("\n", my.ChangeColumn(before, before, before.Column("status"), status, null)));
        List<Dictionary<string, object>> MySql(string sql, params object[] args) => sql.StartsWith("SELECT VERSION()", StringComparison.Ordinal)
            ? new List<Dictionary<string, object>> { new() { ["v"] = "8.4.3" } }
            : new List<Dictionary<string, object>> { new() { ["name"] = "orders_chk_1", ["clause"] = "`status` in (_utf8mb4'open',_utf8mb4'paid')" }, new() { ["name"] = "orders_chk_2", ["clause"] = "`id` > 0" } };
        string modify = string.Join("\n", my.ChangeColumn(before, before, before.Column("status"), status, MySql));
        Assert.Contains("DROP CHECK `orders_chk_1`", modify);
        Assert.DoesNotContain("orders_chk_2", modify);
        List<Dictionary<string, object>> MariaDb(string sql, params object[] args) => new List<Dictionary<string, object>> { new() { ["v"] = "11.4.2-MariaDB" } };
        Assert.DoesNotContain("DROP CHECK", string.Join("\n", my.ChangeColumn(before, before, before.Column("status"), status, MariaDb)));
        string drop = string.Join("\n", my.DropColumn(before, before, before.Column("code")));
        Assert.Contains("DROP INDEX `orders_code_index`", drop);
        Assert.EndsWith("ALTER TABLE `orders` DROP COLUMN `code`", drop);

        // MySQL takes a default for LONGTEXT only as an expression.
        Assert.Equal("('member')", MySqlDialect.MySql.DefaultValue(new Column { Name = "role", Kind = ColumnKind.Text, Default = "member" }));
        Assert.Equal("'member'", MySqlDialect.MySql.DefaultValue(new Column { Name = "role", Kind = ColumnKind.Text, Length = 20, Default = "member" }));
        Assert.Equal("0", MySqlDialect.MySql.DefaultValue(new Column { Name = "n", Kind = ColumnKind.Integer, Default = 0.0 }));
    }

    [Fact]
    public void BrokenModelsTablesAndRowsAreExplained()
    {
        // A model file that fails says where.
        Models(tag: "import KScript.Data\n\nthrow NotFound(message: 'broken model file')\n");
        Assert.Contains("broken model file", Failed("diff"));

        // A kscript_migrations table that isn't one.
        Models(tag: null, user: User.Replace("  posts: Post[]\n", string.Empty), post: Post.Replace("  tags: Tag[]\n", string.Empty));
        Keep("$db->sql('CREATE TABLE kscript_migrations (x int)')");
        Assert.Contains("Query", Failed("status"));
        Keep("$db->sql('DROP TABLE kscript_migrations')");

        // Rows that point nowhere once a migration has run.
        if (Server != null) return;
        Ok("diff");
        Ok("migrate");
        FileOf("database/migrations/2026_12_01_000000_orphans.kscript", "import KScript.Data\n\nMigration {\n  Sql(up: \"INSERT INTO posts (title, views, author_id) VALUES ('x', 0, 999)\")\n}\n");
        Assert.Contains("rows of posts point to rows of users that aren't there", Failed("migrate"));
    }

    [Fact]
    public void MakeWritesAMigrationAndASeeder()
    {
        var made = KScript.Scaffolding.Scaffolder.Make(new KScript.Scaffolding.MakeRequest { Type = "migration", Name = "add bio to users", Root = Project });
        Assert.Equal("database/migrations/2026_11_22_100000_add_bio_to_users.kscript", made.RelativePath.Replace('\\', '/'));
        string text = File.ReadAllText(Path.Combine(Project, made.RelativePath));
        Assert.StartsWith("// Add Bio To Users: run by kscript db migrate", text);
        Assert.Contains("import KScript.Data\n\nMigration {", text);
        Assert.Equal("add_bio_to_users", KScript.Scaffolding.Scaffolder.Example("migration"));
        Assert.Equal("users", KScript.Scaffolding.Scaffolder.Example("seeder"));
        // An empty migration runs, and the seeder made next to it does too.
        Ok("migrate");
        made = KScript.Scaffolding.Scaffolder.Make(new KScript.Scaffolding.MakeRequest { Type = "seeder", Name = "users", Root = Project });
        Assert.Equal("database/seeders/users.kscript", made.RelativePath.Replace('\\', '/'));
        Assert.Contains("import \"../../models/user.kscript\"", File.ReadAllText(Path.Combine(Project, made.RelativePath)));
        Ok("seed");

        // Without a test's clock, files are named after now.
        Assert.True(Math.Abs((KScript.Scaffolding.Scaffolder.Now() - DateTime.Now).TotalMinutes) < 5);
    }

    [Fact]
    public void DiffExplainsABrokenMigration()
    {
        Models();
        FileOf("database/migrations/2026_01_01_000000_bad.kscript", "import KScript.Data\n\nMigration {\n  DropTable(name: 'nope')\n}\n");
        string text = Failed("diff");
        Assert.Contains("2026_01_01_000000_bad: Can't drop the table nope", text);
    }

    [Fact]
    public void ANullDefaultIsntADatabaseDefault()
    {
        Models(user: User.Replace("  active: bool = true\n", "  active: bool = true\n  nick?: string = null\n"));
        Ok("diff");
        Assert.Contains("Field(name: 'nick', type: string, optional)\n", Migration("create"));
    }
}
