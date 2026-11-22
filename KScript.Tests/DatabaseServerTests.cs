using System.Reflection;

using KScript.Data;
using KScript.Runtime;

using MySqlConnector;

using Npgsql;

namespace KScript.Tests;

/// <summary>
/// Postgres, MySQL and MariaDB: connection settings, the SQL each dialect writes, and how their errors read. These run
/// without a server. The ones at the end need one (KSCRIPT_TEST_DATABASE_URL), and pass at once without it; the CI
/// databases job runs every KScript.Data test on each server.
/// </summary>
public sealed class DatabaseServerTests : DataFixture
{
    private static DatabaseSettings Settings(string url) =>
        DatabaseSettings.FromOptions("main", new Dictionary<string, object> { ["url"] = url }, Path.GetTempPath());

    private static DatabaseSettings Settings(Dictionary<string, object> options) =>
        DatabaseSettings.FromOptions("main", options, Path.GetTempPath());

    [Fact]
    public void PostgresUrlsBecomeConnectionStrings()
    {
        DatabaseSettings s = Settings("postgres://kyle:p%40ss;word@db.example.com:6543/app?sslmode=require&Timeout=5");
        Assert.Same(PostgresDialect.Instance, s.Dialect);
        Assert.Equal("Host=db.example.com;Port=6543;Database=app;Username=kyle;Password=\"p@ss;word\";sslmode=require;Timeout=5", s.ConnectionString);
        Assert.Equal("kyle@db.example.com:6543/app", s.Where);
        Assert.Null(s.File);
        Assert.False(s.Memory);

        Assert.Equal("Host=localhost;Port=5432;Database=app", Settings("postgresql:///app").ConnectionString);
        Assert.Equal("Host=localhost;Port=5432", Settings("pgsql://").ConnectionString);
        Assert.Equal("Host=db;Port=5432;Username=u;Password=", Settings("postgres://u:@db").ConnectionString);
        // An option given twice: the url's ?host= wins over the host before it (a Unix socket, say).
        Assert.Equal("Port=5432;Database=app;host=/var/run/postgresql", Settings("postgres://localhost/app?host=%2Fvar%2Frun%2Fpostgresql").ConnectionString);
        Assert.Equal("Host=localhost;Port=5432;Database=app;Pooling=true", Settings("postgres://localhost/app?Pooling").ConnectionString);
    }

    [Fact]
    public void MySqlAndMariaDbUrls()
    {
        DatabaseSettings mysql = Settings("mysql://root:secret@[::1]:3307/shop");
        Assert.Same(MySqlDialect.MySql, mysql.Dialect);
        Assert.Equal("Server=::1;Port=3307;Database=shop;User ID=root;Password=secret", mysql.ConnectionString);
        Assert.Equal("root@::1:3307/shop", mysql.Where);

        DatabaseSettings maria = Settings("mariadb://app@localhost/shop");
        Assert.Same(MySqlDialect.MariaDb, maria.Dialect);
        Assert.Equal("Server=localhost;Port=3306;Database=shop;User ID=app", maria.ConnectionString);
        Assert.Equal("mariadb", maria.Dialect.Name);
    }

    [Fact]
    public void ServersFromConfigOptions()
    {
        DatabaseSettings s = Settings(new Dictionary<string, object>
        {
            ["driver"] = "postgres",
            ["host"] = "db",
            ["port"] = 6000.0,
            ["database"] = "app",
            ["user"] = "kyle",
            ["password"] = " has spaces ",
            ["options"] = new Dictionary<string, object> { ["sslmode"] = "require" },
        });
        Assert.Equal("Host=db;Port=6000;Database=app;Username=kyle;Password=\" has spaces \";sslmode=require", s.ConnectionString);

        DatabaseSettings m = Settings(new Dictionary<string, object> { ["driver"] = "MySQL", ["username"] = "a", ["password"] = "x\"y" });
        Assert.Equal("Server=localhost;Port=3306;User ID=a;Password=\"x\"\"y\"", m.ConnectionString);
        Assert.Equal("Server=localhost;Port=3306", Settings(new Dictionary<string, object> { ["driver"] = "mariadb" }).ConnectionString);

        KScriptException e = Assert.Throws<KScriptException>(() => Settings(new Dictionary<string, object> { ["driver"] = "postgres", ["port"] = "high" }));
        Assert.Equal("Config", e.Type);
        Assert.Contains("'high' as its port", e.Message);
        Assert.Contains("as its port", Assert.Throws<KScriptException>(() => Settings("mysql://localhost:0/x")).Message);
    }

    [Fact]
    public void UrlsNameTheDatabaseFirst()
    {
        KScriptException e = Assert.Throws<KScriptException>(() => Settings("localhost/app"));
        Assert.Equal("Config", e.Type);
        Assert.Contains("postgres://user:password@localhost/app", e.Message);
    }

    /// <summary>The SQL a query writes on a database, without connecting to it.</summary>
    private string[] SqlOn(string url, string code)
    {
        FileOf("config.yaml", $"database:\n  connections:\n    main:\n      url: \"{url}\"\n");
        Environment.SetEnvironmentVariable("DATABASE_URL", null);
        string output = Run("""
            model User {
              timestamps: yes
              unique: [email]
              index: [[name, age]]
              name: string(max: 40)
              email: email
              age: int?
              active: bool = true
              bio: string?
              tags: string[] = []
              born: date?
            }

            """ + code);
        File.Delete(Path.Combine(Project, "config.yaml"));
        return output.TrimEnd('\n').Split('\n');
    }

    private const string Queries = """
        echo $User->where(fn(u) => $u.age > 18 and $u.name->contains('50%'))->order_by(name: desc)->limit(5)->offset(10)->sql().sql
        echo $User->where(fn(u) => $u.name->matches(/^a.b$/im) or $u.email->ends_with('.org'))->offset(3)->sql().sql
        echo $User->where(fn(u) => ($u.name + '!')->length() > 3)->lock_for_update()->skip_locked()->sql().sql
        echo $User->where(fn(u) => $u.name->test(/x/sx))->order_by(age)->sql().sql
        echo @db(visits)->where(n: 1)->sql().sql
        """;

    [Fact]
    public void PostgresSql()
    {
        string[] lines = SqlOn("postgres://nobody@127.0.0.1:1/none", Queries + "\necho $db\necho [$db.driver, $db.file ?? 'no file']");
        Assert.Equal("SELECT * FROM \"users\" WHERE (\"age\" > @p1) AND (CAST(\"name\" AS TEXT) COLLATE \"default\" ILIKE @p2 ESCAPE '\\') ORDER BY \"name\" DESC NULLS LAST LIMIT 5 OFFSET 10", lines[0]);
        Assert.Equal("SELECT * FROM \"users\" WHERE (CAST(\"name\" AS TEXT) COLLATE \"default\" ~ @p1) OR (CAST(\"email\" AS TEXT) COLLATE \"default\" ILIKE @p2 ESCAPE '\\') OFFSET 3", lines[1]);
        Assert.Equal("SELECT * FROM \"users\" WHERE LENGTH(\"name\" || @p1) > @p2 FOR UPDATE SKIP LOCKED", lines[2]);
        Assert.Equal("SELECT * FROM \"users\" WHERE CAST(\"name\" AS TEXT) COLLATE \"default\" ~ @p1 ORDER BY \"age\" ASC NULLS FIRST", lines[3]);
        Assert.Equal("SELECT * FROM \"visits\" WHERE \"n\" = @p1", lines[4]);
        Assert.Equal("postgres database 'main' (nobody@127.0.0.1:1/none)", lines[5]);
        Assert.Equal("postgres, no file", lines[6]);
    }

    [Fact]
    public void MySqlSql()
    {
        string[] lines = SqlOn("mysql://nobody@127.0.0.1:1/none", Queries);
        Assert.Equal("SELECT * FROM `users` WHERE (`age` > @p1) AND (LOWER(`name`) LIKE LOWER(@p2) ESCAPE '\\\\') ORDER BY `name` DESC LIMIT 5 OFFSET 10", lines[0]);
        Assert.Equal("SELECT * FROM `users` WHERE (`name` REGEXP @p1) OR (LOWER(`email`) LIKE LOWER(@p2) ESCAPE '\\\\') LIMIT 18446744073709551615 OFFSET 3", lines[1]);
        Assert.Equal("SELECT * FROM `users` WHERE CHAR_LENGTH(CONCAT(`name`, @p1)) > @p2 FOR UPDATE SKIP LOCKED", lines[2]);
        Assert.Equal("SELECT * FROM `users` WHERE `name` REGEXP @p1 ORDER BY `age` ASC", lines[3]);
    }

    [Fact]
    public void RegexFlagsBecomeEmbeddedOptions()
    {
        var pg = new SqlWriter(PostgresDialect.Instance);
        foreach ((string flags, string options) in new[] { ("", "p"), ("i", "pi"), ("m", "n"), ("ms", "w"), ("s", "s"), ("ix", "pix") })
        {
            Assert.True(RegexValue.TryParse("/a.b/" + flags, out RegexValue regex, out _));
            SqlStatement s = new SqlWriter(PostgresDialect.Instance).Expr(new SqlMatch(new SqlColumn("x"), regex)).Build();
            Assert.Equal("(?" + options + ")a.b", s.Parameters[0]);
        }
        Assert.True(RegexValue.TryParse("/a.b/ims", out RegexValue my, out _));
        Assert.Equal("(?ims)a.b", new SqlWriter(MySqlDialect.MySql).Expr(new SqlMatch(new SqlColumn("x"), my)).Build().Parameters[0]);
        Assert.True(RegexValue.TryParse("/a.b/", out RegexValue plain, out _));
        Assert.Equal("a.b", new SqlWriter(MySqlDialect.MySql).Expr(new SqlMatch(new SqlColumn("x"), plain)).Build().Parameters[0]);
        Assert.Equal("/a.b/", new SqlWriter(SqliteDialect.Instance).Expr(new SqlMatch(new SqlColumn("x"), plain)).Build().Parameters[0]);
        Assert.Empty(pg.Build().Parameters);
    }

    [Fact]
    public void TablesForEachDatabase()
    {
        SqlOn("postgres://nobody@127.0.0.1:1/none", "echo 1");
        ModelInfo user = ModelInfo.OfTable("users");
        List<string> pg = PostgresDialect.Instance.CreateTable(user, ifMissing: true).ToList();
        Assert.Equal("CREATE COLLATION IF NOT EXISTS \"kscript_nocase\" (provider = icu, locale = 'und-u-ks-level2', deterministic = false)", pg[0]);
        Assert.Contains("  \"id\" BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,", pg[1]);
        Assert.Contains("  \"email\" TEXT COLLATE \"kscript_nocase\" NOT NULL,", pg[1]);
        Assert.Contains("  \"active\" BOOLEAN,", pg[1]);
        Assert.Contains("  \"born\" TIMESTAMP(6),", pg[1]);
        Assert.Contains("  \"age\" BIGINT,", pg[1]);
        Assert.Equal("CREATE UNIQUE INDEX IF NOT EXISTS \"ux_users_email\" ON \"users\" (\"email\")", pg[2]);
        Assert.Equal("CREATE INDEX IF NOT EXISTS \"ix_users_name_age\" ON \"users\" (\"name\", \"age\")", pg[3]);

        string my = Assert.Single(MySqlDialect.MySql.CreateTable(user, ifMissing: false));
        Assert.StartsWith("CREATE TABLE `users` (\n  `id` BIGINT AUTO_INCREMENT PRIMARY KEY,\n  `name` VARCHAR(40) NOT NULL,\n  `email` VARCHAR(254) COLLATE utf8mb4_unicode_ci NOT NULL,", my);
        Assert.Contains("  `bio` LONGTEXT,", my);
        Assert.Contains("  `tags` LONGTEXT,", my);
        Assert.Contains("  `born` DATETIME(6),", my);
        Assert.Contains("  UNIQUE KEY `ux_users_email` (`email`),\n  KEY `ix_users_name_age` (`name`, `age`)\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin", my);

        // Only a table with an email needs the collation.
        SqlOn("postgres://nobody@127.0.0.1:1/none", "model Tag {\n  name: string\n}\necho 1");
        Assert.DoesNotContain(PostgresDialect.Instance.CreateTable(ModelInfo.OfTable("tags"), ifMissing: true), s => s.Contains("COLLATION"));
    }

    [Fact]
    public void MySqlColumnTypes()
    {
        string Type(ColumnKind kind, int? length = null, bool indexed = false) =>
            MySqlDialect.MySql.ColumnType(new Column { Name = "c", Kind = kind, Length = length }, indexed);
        Assert.Equal("VARCHAR(36)", Type(ColumnKind.Uuid));
        Assert.Equal("VARCHAR(255)", Type(ColumnKind.Handle));
        Assert.Equal("VARCHAR(255)", Type(ColumnKind.Enum));
        Assert.Equal("VARCHAR(255)", Type(ColumnKind.Password));
        Assert.Equal("VARCHAR(255)", Type(ColumnKind.Text, indexed: true));
        Assert.Equal("LONGTEXT", Type(ColumnKind.Text, length: 100000));
        Assert.Equal("DOUBLE", Type(ColumnKind.Real));
        Assert.Equal("BOOLEAN", Type(ColumnKind.Bool));
        Assert.Equal("VARCHAR(100) COLLATE utf8mb4_unicode_ci", Type(ColumnKind.Email, length: 100));
        Assert.Equal("DOUBLE PRECISION", PostgresDialect.Instance.ColumnType(new Column { Name = "c", Kind = ColumnKind.Real }, false));
        Assert.Equal("TEXT", PostgresDialect.Instance.ColumnType(new Column { Name = "c", Kind = ColumnKind.Json }, false));
    }

    [Fact]
    public void LongIndexNamesAreCutWithAHash()
    {
        string name = Dialect.IndexName("a_table_with_a_rather_long_name", new[] { "first_long_column", "second_long_column" }, true);
        Assert.Equal(60, name.Length);
        Assert.StartsWith("ux_a_table_with_a_rather_long_name_first_", name);
        Assert.NotEqual(name, Dialect.IndexName("a_table_with_a_rather_long_name", new[] { "first_long_column", "second_long_columns" }, true));
        Assert.Equal("ix_t_a", Dialect.IndexName("t", new[] { "a" }, false));
    }

    [Fact]
    public void UpsertsForEachDatabase()
    {
        const string insert = "INSERT INTO t (k, v) VALUES (@p1, @p2)";
        Assert.Equal(insert + " ON CONFLICT (\"k\") DO UPDATE SET \"v\" = excluded.\"v\"", PostgresDialect.Instance.Upsert(insert, "k", new[] { "v" }));
        Assert.Equal(insert + " ON CONFLICT (\"k\") DO NOTHING", SqliteDialect.Instance.InsertOrIgnore(insert, "k"));
        Assert.Equal(insert + " ON DUPLICATE KEY UPDATE `v` = VALUES(`v`)", MySqlDialect.MySql.Upsert(insert, "k", new[] { "v" }));
        Assert.Equal("INSERT IGNORE INTO t (k, v) VALUES (@p1, @p2)", MySqlDialect.MySql.InsertOrIgnore(insert, "k"));
        Assert.True(PostgresDialect.Instance.ConditionalUpsert);
        Assert.False(MySqlDialect.MySql.ConditionalUpsert);
        Assert.Equal(" () VALUES ()", MySqlDialect.MySql.DefaultValues);
        Assert.Equal(" LOCK IN SHARE MODE", MySqlDialect.MySql.Lock("share", false));
        Assert.Equal(" FOR SHARE", PostgresDialect.Instance.Lock("share", false));
        Assert.Equal(string.Empty, SqliteDialect.Instance.Lock("update", true));
    }

    [Fact]
    public void DatesAreWrittenAsTheirClockTime()
    {
        var utc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Assert.Equal(DateTimeKind.Unspecified, ((DateTime)PostgresDialect.Instance.Date(utc)).Kind);
        Assert.Equal(utc.Ticks, ((DateTime)MySqlDialect.MySql.Date(utc)).Ticks);
        Assert.Equal("2026-01-02 03:04:05.0000000", SqliteDialect.Instance.Date(utc));
        Assert.Equal(0, Rows.Now().Ticks % 10);
    }

    [Fact]
    public void PostgresErrors()
    {
        PostgresException Error(string state, string table = null, string constraint = null) =>
            new PostgresException("it broke", "ERROR", "ERROR", state, tableName: table, constraintName: constraint);
        var pg = PostgresDialect.Instance;

        SqlOn("postgres://nobody@127.0.0.1:1/none", "echo 1");
        KScriptException unique = pg.Translate(Error("23505", constraint: "ux_users_email"), "users");
        Assert.Equal("UniqueViolation", unique.Type);
        Assert.Equal("A User already has that email.", unique.Message);
        Assert.Equal(new List<object> { "id" }, pg.Translate(Error("23505", "users", "users_pkey"), null).Fields["fields"]);
        Assert.Equal(new List<object> { "code" }, pg.Translate(Error("23505", "rooms", "ux_rooms_code"), null).Fields["fields"]);
        Assert.Equal("A row already has that value.", pg.Translate(Error("23505", "rooms", "something_else"), null).Message);
        Assert.Equal("A row already has that value.", pg.Translate(Error("23505"), null).Message);
        Assert.Equal("ForeignKeyViolation", pg.Translate(Error("23503"), "users").Type);
        KScriptException other = pg.Translate(Error("42P01"), null);
        Assert.Equal("QueryError", other.Type);
        Assert.Equal("The database refused the query: it broke", other.Message);
        Assert.Null(pg.Translate(new InvalidOperationException("no"), null));

        Assert.True(pg.IsBusy(Error("40P01")));
        Assert.True(pg.IsBusy(Error("40001")));
        Assert.False(pg.IsBusy(Error("23505")));
        Assert.False(pg.IsBusy(new IOException()));
    }

    private static MySqlException MySqlError(int number, string message)
    {
        // MySqlConnector makes its exceptions itself; tests make one the same way.
        ConstructorInfo ctor = typeof(MySqlException).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(MySqlErrorCode), typeof(string), typeof(string) }));
        return (MySqlException)ctor.Invoke(new object[] { (MySqlErrorCode)number, "23000", message });
    }

    [Fact]
    public void MySqlErrors()
    {
        var my = MySqlDialect.MySql;
        SqlOn("mysql://nobody@127.0.0.1:1/none", "echo 1");
        Assert.Equal("A User already has that email.", my.Translate(MySqlError(1062, "Duplicate entry 'a@b.org' for key 'users.ux_users_email'"), "users").Message);
        Assert.Equal(new List<object> { "email" }, my.Translate(MySqlError(1062, "Duplicate entry 'a@b.org' for key 'ux_users_email'"), "users").Fields["fields"]);
        Assert.Equal(new List<object> { "id" }, my.Translate(MySqlError(1062, "Duplicate entry '1' for key 'PRIMARY'"), "users").Fields["fields"]);
        Assert.Equal("A row already has that value.", my.Translate(MySqlError(1062, "Duplicate entry"), null).Message);
        Assert.Equal("ForeignKeyViolation", my.Translate(MySqlError(1452, "Cannot add or update a child row"), null).Type);
        Assert.Equal("ForeignKeyViolation", my.Translate(MySqlError(1451, "Cannot delete or update a parent row"), null).Type);
        Assert.Equal("The database refused the query: no such table", my.Translate(MySqlError(1146, "no such table"), null).Message);
        Assert.Null(my.Translate(new IOException(), null));
        Assert.True(my.IsBusy(MySqlError(1213, "Deadlock")));
        Assert.True(my.IsBusy(MySqlError(1205, "Lock wait timeout")));
        Assert.False(my.IsBusy(MySqlError(1062, "Duplicate")));
    }

    [Fact]
    public void AServerThatIsntThereIsAConnectionError()
    {
        foreach (string url in new[] { "postgres://nobody@127.0.0.1:1/none?Timeout=2", "mysql://nobody@127.0.0.1:1/none?ConnectionTimeout=2" })
        {
            FileOf("config.yaml", $"database:\n  connections:\n    main:\n      url: \"{url}\"\n");
            KScriptException e = Fails("echo $db->tables()");
            Assert.Equal("ConnectionError", e.Type);
            Assert.StartsWith("Can't open the ", e.Message);
            Assert.Contains("database 'main' (nobody@127.0.0.1:1/none)", e.Message);
        }
        FileOf("config.yaml", "database:\n  connections:\n    main:\n      url: \"postgres://localhost/x?NoSuchOption=1\"\n");
        Assert.Equal("ConnectionError", Fails("echo $db->tables()").Type);
    }

    // ---- on a server ------------------------------------------------------------------------------------------------

    [Fact]
    public void ServersRetryBusyTransactions()
    {
        if (Server == null) return;
        Run("echo 1");
        var host = KScriptHost.ForText(new StringWriter());
        var interpreter = new Interpreter(host);
        Database db = Database.For(host, Project, null);
        Exception inner = db.Dialect is PostgresDialect ? new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01") : MySqlError(1213, "Deadlock");
        int calls = 0;
        Assert.Equal(3, db.InTransaction(interpreter, () =>
        {
            calls++;
            if (calls < 3) throw new KScriptException("deadlock", "QueryError", inner);
            return 3;
        }, retries: 2));
    }

    [Fact]
    public void ServersKeepCaseInTextButNotInEmails()
    {
        if (Server == null) return;
        string[] lines = Lines("""
            model Person {
              unique: [email]
              name: string
              email: email
              note: string?
            }
            $db->create_table($Person)
            $Person->create(name: 'Ada', email: 'Ada@Example.com', note: @json('"two\nlines"'))
            echo $Person->where(name: 'ada')->count()
            echo $Person->where(email: 'ADA@example.com')->count()
            echo $Person->where(fn(p) => $p.name->contains('AD'))->count()
            echo $Person->where(fn(p) => $p.email->starts_with('ada'))->count()
            echo $Person->where(fn(p) => $p.name->matches(/^ada$/i))->count()
            echo $Person->where(fn(p) => $p.name->matches(/^ada$/))->count()
            echo $Person->where(fn(p) => $p.note->matches(/^lines/m))->count()
            echo $Person->where(fn(p) => $p.note->matches(/two.lines/))->count()
            echo $Person->where(fn(p) => $p.note->matches(/two.lines/s))->count()
            echo $Person->where(fn(p) => $p.name + '!' == 'Ada!')->count()
            echo $Person->where(fn(p) => $p.name->length() == 3)->count()
            try {
              $Person->create(name: 'Ada 2', email: 'ADA@example.com')
            } catch UniqueViolation as e {
              echo $e.message
            }
            """);
        Assert.Equal(new[] { "0", "1", "1", "1", "1", "0", "1", "0", "1", "1", "1", "A Person already has that email." }, lines);
    }

    [Fact]
    public void ServersNumberRowsAndLockThem()
    {
        if (Server == null) return;
        string[] lines = Lines($$"""
            fn show(rows, page) {
              echo "{ $page }: { $rows->count() }"
            }
            $db->sql('create table hits (id {{AutoId}}, n integer)')
            echo @db(hits)->insert([{ n: 1 }, { n: 2 }, { n: 3 }])
            transaction {
              echo @db(hits)->where(n: 2)->lock_for_update()->skip_locked()->pluck(id)
            }
            echo @db(hits)->offset(1)->pluck(n)
            @db(hits)->chunk(2, @show)
            echo $db.driver
            """);
        Assert.Equal("3", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("2, 3", lines[2]);
        Assert.Equal(new[] { "0: 2", "1: 1" }, lines.Skip(3).Take(2));
    }

    [Fact]
    public void PostgresCommitsThatFailAreErrors()
    {
        if (Server == null || !Server.StartsWith("postgres", StringComparison.Ordinal)) return;
        KScriptException e = Fails("""
            $db->sql('create table teams (id integer primary key)')
            $db->sql('create table members (id integer primary key, team_id integer references teams(id) deferrable initially deferred)')
            transaction {
              $db->sql('insert into members (id, team_id) values (1, 5)')
            }
            """);
        Assert.Equal("ForeignKeyViolation", e.Type);
    }
}
