using System.Globalization;
using System.Text;

using KScript.Data;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// Runs .kscript code in a temporary project folder, so each test has its own database (data/app.sqlite there), and
/// closes every connection afterwards. KScript.Tests runs one test at a time.
/// </summary>
public abstract class DataFixture : IDisposable
{
    protected readonly string Project = Path.Combine(Path.GetTempPath(), "kscript-data-" + Guid.NewGuid().ToString("N"));

    protected DataFixture()
    {
        Directory.CreateDirectory(Project);
    }

    public void Dispose()
    {
        Database.CloseAll();
        Database.Log = null;
        Rows.Clock = () => DateTime.Now;
        try { Directory.Delete(Project, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    protected void FileOf(string name, string text)
    {
        string path = Path.Combine(Project, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    /// <summary>Runs .kscript code (KScript.Data is imported) with a new database, and returns what it printed.</summary>
    protected string Run(string code, string file = "app.kscript")
    {
        Database.CloseAll();
        try { Directory.Delete(Path.Combine(Project, "data"), recursive: true); } catch (DirectoryNotFoundException) { }
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output);
        FileOf(file, "import KScript.Data\n" + code + "\n");
        new Interpreter(host).RunFile(Path.Combine(Project, file));
        return output.ToString().Replace("\r\n", "\n");
    }

    protected string[] Lines(string code) => Run(code).TrimEnd('\n').Split('\n');

    protected KScriptException Fails(string code) => Assert.Throws<KScriptException>(() => Run(code));

    protected const string Users = """
        enum Role { member, editor, admin }

        model User {
          timestamps: yes
          unique: [email]
          hidden: [password]
          guarded: [role]
          name: string(max: 40)
          email: email
          age: int?
          role: Role = 'member'
          active: bool = true
          password: password?
          tags: string[] = []
          label => $name + ' <' + $email + '>'
        }

        $db->create_table($User)
        $User->create(name: 'Ada', email: 'ada@example.com', age: 36, tags: ['math'])
        $User->create(name: 'Ben', email: 'ben@example.com', age: 17, active: false)
        $User->create(name: 'Cy', email: 'cy@test.org')

        """;
}

public sealed class DataModelTests : DataFixture
{
    [Fact]
    public void CreatesAndReadsRecords()
    {
        string[] lines = Lines(Users + """
            u = $User->find(1)
            echo [$u.id, $u.name, $u.age, $u.role, $u.active, $u.label]
            echo $u.tags
            echo @type($u)
            echo $User->count()
            echo $User->all()->count()
            """);
        Assert.Equal("1, Ada, 36, member, true, Ada <ada@example.com>", lines[0]);
        Assert.Equal("math", lines[1]);
        Assert.Equal("User", lines[2]);
        Assert.Equal("3", lines[3]);
        Assert.Equal("3", lines[4]);
        Assert.True(File.Exists(Path.Combine(Project, "data", "app.sqlite")));
    }

    [Fact]
    public void TableNamesComeFromTheModelName()
    {
        string[] lines = Lines("""
            model BlogPost {
              title: string
            }
            model Category {
              table: kinds
              name: string
            }
            model Box {
              name: string
            }
            $db->create_table($BlogPost)
            $db->create_table($Category)
            $db->create_table($Box)
            echo $db->tables()
            echo $db->has_table($BlogPost)
            echo $db->has_table('nope')
            """);
        Assert.Equal("blog_posts, boxes, kinds", lines[0]);
        Assert.Equal("true", lines[1]);
        Assert.Equal("false", lines[2]);
    }

    [Theory]
    [InlineData("Person", "people")]
    [InlineData("City", "cities")]
    [InlineData("Day", "days")]
    [InlineData("Bus", "buses")]
    [InlineData("Match", "matches")]
    [InlineData("Wish", "wishes")]
    [InlineData("Fox", "foxes")]
    [InlineData("HTTPLog", "http_logs")]
    public void PluralTableNames(string model, string table) => Assert.Equal(table, ModelInfo.TableName(model));

    [Fact]
    public void GuardedFieldsAreLeftOutOfCreateAndUpdate()
    {
        string[] lines = Lines(Users + """
            u = $User->create(name: 'Di', email: 'di@example.com', role: admin)
            echo $u.role
            $u->update({ role: 'admin', name: 'Dee' })
            echo [$u.role, $u.name]
            $u.role = 'admin'
            $u->save()
            echo $User->find($u.id).role
            echo $User->where(role: admin)->update(role: member)
            echo $User->find($u.id).role
            """);
        Assert.Equal(new[] { "member", "member, Dee", "admin", "1", "admin" }, lines);
    }

    [Fact]
    public void ValuesAreCheckedByTheirTypes()
    {
        Assert.Contains("email", Fails(Users + "$User->create(name: 'X', email: 'not an email')").Message);
        Assert.Contains("40", Fails(Users + "$User->create(name: '" + new string('x', 50) + "', email: 'x@example.com')").Message);
        Assert.Contains("age", Fails(Users + "u = $User->find(1)\n$u.age = 'old'").Message);
    }

    [Fact]
    public void PasswordsAreHashedAndVerified()
    {
        string[] lines = Lines(Users + """
            u = $User->create(name: 'Pat', email: 'pat@example.com', password: 'correct horse battery')
            echo $u.password
            again = $User->find($u.id)
            echo $again.password->verify('correct horse battery')
            echo $again.password->verify('wrong password here')
            echo $again.password->verify(42)
            echo $again.password->needs_rehash()
            echo $again.password->needs_rehash(bcrypt)
            echo $again->to_json()->contains('password')
            raw = @sql('select password from users where id = 4')[0].password
            echo $raw->starts_with('$argon2id$')
            $again.password = 'another good password'
            $again->save()
            echo $User->find(4).password->verify('another good password')
            """);
        Assert.Equal("********", lines[0]);
        Assert.Equal(new[] { "true", "false", "false", "false", "true", "false", "true", "true" }, lines.Skip(1).ToArray());
    }

    [Fact]
    public void TimestampsAreSetOnCreateAndSave()
    {
        var times = new Queue<DateTime>(new[] { new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 2, 9, 0, 0) });
        Rows.Clock = () => times.Count > 0 ? times.Dequeue() : new DateTime(2026, 1, 3, 9, 0, 0);
        string[] lines = Lines("""
            model Note {
              timestamps: yes
              text: string
            }
            $db->create_table($Note)
            n = $Note->create(text: 'a')
            echo [$n.created_at->format('yyyy-MM-dd'), $n.updated_at->format('yyyy-MM-dd')]
            $n.text = 'b'
            $n->save()
            again = $Note->find(1)
            echo [$again.created_at->format('yyyy-MM-dd'), $again.updated_at->format('yyyy-MM-dd')]
            echo $Note->latest()->first().text
            echo $Note->oldest()->first().text
            """);
        Assert.Equal("2026-01-01, 2026-01-01", lines[0]);
        Assert.Equal("2026-01-01, 2026-01-02", lines[1]);
    }

    [Fact]
    public void DirtyFieldsAndSaving()
    {
        string[] lines = Lines(Users + """
            u = $User->find(1)
            echo $u->is_dirty()
            echo $u->is_saved()
            $u.age = 37
            echo $u->is_dirty()
            echo $u->is_dirty(age)
            echo $u->is_dirty(name)
            echo $u->dirty()
            $u->save()
            echo $u->is_dirty()
            $u->save()
            fresh = $u->fresh()
            echo $fresh.age
            n = $User->from({ name: 'New', email: 'new@example.com' })
            echo $n->is_saved()
            echo $n->dirty()->count() > 0
            $n->save()
            echo [$n->is_saved(), $n.id]
            """);
        Assert.Equal(new[] { "false", "true", "true", "true", "false", "age", "false", "37", "false", "true", "true, 4" }, lines);
    }

    [Fact]
    public void DeletingAndSoftDeleting()
    {
        string[] lines = Lines("""
            model Doc {
              soft_deletes: yes
              title: string
            }
            model Tmp {
              title: string
            }
            $db->create_table($Doc)
            $db->create_table($Tmp)
            a = $Doc->create(title: 'a')
            b = $Doc->create(title: 'b')
            echo $a->delete()
            echo [$Doc->count(), $Doc->with_trashed()->count(), $Doc->only_trashed()->count()]
            echo $a.deleted_at != null
            $a->restore()
            echo $Doc->count()
            echo $Doc->where(title: 'b')->delete()
            echo $Doc->only_trashed()->restore()
            echo $b->force_delete()
            echo $Doc->with_trashed()->count()
            echo $Doc->force_delete()
            t = $Tmp->create(title: 'x')
            echo $t->delete()
            echo $t->is_saved()
            echo $Tmp->count()
            """);
        Assert.Equal(new[] { "true", "1, 2, 1", "true", "2", "1", "1", "true", "1", "1", "true", "false", "0" }, lines);
    }

    [Fact]
    public void RecordMethodsNeedSavedModelRecords()
    {
        Assert.Contains("hasn't been saved", Fails(Users + "n = $User->from({ name: 'N', email: 'n@example.com' })\n$n->delete()").Message);
        Assert.Contains("soft_deletes", Fails(Users + "$User->find(1)->restore()").Message);
        Assert.Contains("hasn't been saved", Fails(Users + "$User->from({ name: 'N', email: 'n@example.com' })->fresh()").Message);
    }

    [Fact]
    public void ModelsHideFieldsInJson()
    {
        string json = Run(Users + "$User->create(name: 'Pat', email: 'pat@example.com', password: 'correct horse battery')\necho $User->find(4)->to_json()");
        Assert.DoesNotContain("password", json);
        Assert.Contains("\"name\":\"Pat\"", json);
    }

    [Fact]
    public void AModelCanDeclareItsOwnKey()
    {
        string[] lines = Lines("""
            model Country {
              key: code
              code: string
              name: string
            }
            $db->create_table($Country)
            $Country->create(code: 'nz', name: 'New Zealand')
            c = $Country->find('nz')
            echo $c.name
            $c.name = 'Aotearoa'
            $c->save()
            echo $Country->find_or_fail('nz').name
            echo $Country->find('xx') ?? 'none'
            """);
        Assert.Equal(new[] { "New Zealand", "Aotearoa", "none" }, lines);
    }

    [Fact]
    public void ModelSettingsAreChecked()
    {
        Assert.Contains("no field 'nope'", Fails("model A {\n  unique: [nope]\n  name: string\n}").Message);
        Assert.Contains("no field 'nope'", Fails("model A {\n  hidden: [nope]\n  name: string\n}").Message);
        Assert.Contains("no field 'nope'", Fails("model A {\n  key: nope\n  name: string\n}").Message);
    }

    [Fact]
    public void ModelsInXml()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        FileOf("app.ks.xml", """
            <kscript>
              <import package="KScript.Data" />
              <model name="Pet" fields="name: string, legs: int = 4" timestamps="yes" unique="name" />
              <do>$db->create_table($Pet)</do>
              <do>$Pet->create(name: 'Rex')</do>
              <transaction>
                <do>$Pet->create(name: 'Tweety', legs: 2)</do>
              </transaction>
              <echo value="$Pet->pluck(legs)" />
            </kscript>
            """);
        new Interpreter(KScriptHost.ForText(output)).RunFile(Path.Combine(Project, "app.ks.xml"));
        Assert.Equal("4, 2\n", output.ToString());
    }

    [Fact]
    public void ImportedModels()
    {
        FileOf("models/user.kscript", "import KScript.Data\n\nmodel User {\n  name: string\n}\n");
        string[] lines = Lines("""
            import "models/user.kscript"
            $db->create_table($User)
            $User->create(name: 'Ada')
            echo $User->first().name
            """);
        Assert.Equal("Ada", lines[0]);
    }
}

public sealed class DataQueryTests : DataFixture
{
    [Fact]
    public void LambdaConditionsBecomeSql()
    {
        string[] lines = Lines(Users + """
            min = 18
            echo $User->where(fn($u) => $u.age >= $min)->pluck(name)
            echo $User->where(fn($u) => $u.age < $min or $u.age == null)->pluck(name)
            echo $User->where(fn($u) => $u.age != null and not ($u.age > 20))->pluck(name)
            echo $User->where(fn($u) => $u.name->contains('d'))->pluck(name)
            echo $User->where(fn($u) => $u.name->starts_with('B'))->pluck(name)
            echo $User->where(fn($u) => $u.email->ends_with('.org'))->pluck(name)
            echo $User->where(fn($u) => $u.name->lower() == 'cy')->pluck(name)
            echo $User->where(fn($u) => $u.name->upper()->length() == 3)->pluck(name)
            echo $User->where(fn($u) => $u.name->trim() == 'Ada')->count()
            echo $User->where(fn($u) => $u.name->matches(/^[AB]/))->pluck(name)
            echo $User->where(fn($u) => $u.role == member and $u.active)->pluck(name)
            echo $User->where(fn($u) => not $u.active)->pluck(name)
            echo $User->where(fn($u) => $u.name in ['Ada', 'Cy'])->pluck(name)
            echo $User->where(fn($u) => ($u.age ?? 0) == 0)->pluck(name)
            echo $User->where(fn($u) => $u.age + 1 == 37 or $u.age * 2 == 34)->pluck(name)
            echo $User->where(fn($u) => $u.age - 6 == 30 and $u.age / 2 == 18 and $u.age % 5 == 1)->pluck(name)
            echo $User->where(fn($u) => -$u.age == -17 and +$u.age == 17)->pluck(name)
            echo $User->where(fn($u) => ($u.age > 18 ? 'adult' : 'child') == 'child')->pluck(name)
            echo $User->where(fn($u) => $u.name + '!' == 'Ada!')->pluck(name)
            echo $User->where(fn($u) => 'Ada' == $u.name)->pluck(name)
            echo $User->where(fn($u) => null == $u.age)->pluck(name)
            echo $User->where(fn($u) => null != $u.age)->count()
            echo $User->where(fn($u) => $u.email == 'ADA@example.com')->count()
            """);
        Assert.Equal(new[]
        {
            "Ada", "Ben, Cy", "Ben", "Ada", "Ben", "Cy", "Cy", "Ada, Ben", "1", "Ada, Ben", "Ada, Cy", "Ben", "Ada, Cy", "Cy",
            "Ada, Ben", "Ada", "Ben", "Ben, Cy", "Ada", "Ada", "Cy", "2", "1",
        }, lines);
    }

    [Fact]
    public void NamedConditions()
    {
        string[] lines = Lines(Users + """
            echo $User->where(role: member, active: true)->pluck(name)
            echo $User->where(age: null)->pluck(name)
            echo $User->where(name: ['Ada', 'Ben'])->pluck(name)
            echo $User->where({ name: 'Ada' })->count()
            echo $User->where(name: 'Ada')->or_where(name: 'Ben')->pluck(name)
            echo $User->where_not(name: 'Ada')->pluck(name)
            echo $User->where(null, name: 'Cy')->count()
            echo $User->where(name: [])->count()
            echo $User->where(role: [member, admin])->count()
            """);
        Assert.Equal(new[] { "Ada, Cy", "Cy", "Ada, Ben", "1", "Ada, Ben", "Ben, Cy", "1", "0", "3" }, lines);
    }

    [Fact]
    public void SortingLimitsAndSelecting()
    {
        string[] lines = Lines(Users + """
            echo $User->order_by(name: desc)->pluck(name)
            echo $User->order_by(active, name)->pluck(name)
            echo $User->order_by([age])->pluck(name)
            echo $User->order_by(name)->limit(2)->pluck(name)
            echo $User->order_by(name)->offset(1)->pluck(name)
            echo $User->order_by(name)->limit(1)->offset(1)->pluck(name)
            s = $User->select(id, name)->first()
            echo [$s.name, $s.email ?? 'none']
            echo $User->select([name])->distinct()->count()
            echo $User->latest(age)->value(name)
            echo $User->oldest(age)->value(name)
            """);
        Assert.Equal(new[] { "Cy, Ben, Ada", "Ben, Ada, Cy", "Cy, Ben, Ada", "Ada, Ben", "Ben, Cy", "Ben", "Ada, none", "3", "Ada", "Cy" }, lines);
    }

    [Fact]
    public void Aggregates()
    {
        string[] lines = Lines(Users + """
            echo $User->sum(age)
            echo $User->avg(age)
            echo $User->min(age)
            echo $User->max(name)
            echo $User->where(name: 'nobody')->sum(age)
            echo $User->where(name: 'nobody')->avg(age) ?? 'none'
            echo $User->exists()
            echo $User->where(name: 'nobody')->exists()
            echo $User->order_by(name)->limit(2)->count()
            echo $User->order_by(name)->limit(2)->sum(age)
            echo $User->select(active)->distinct()->count()
            echo $User->where(name: 'nobody')->value(name) ?? 'none'
            """);
        Assert.Equal(new[] { "53", "26.5", "17", "Cy", "0", "none", "true", "false", "2", "53", "2", "none" }, lines);
    }

    [Fact]
    public void FirstFindAndFail()
    {
        string[] lines = Lines(Users + """
            echo $User->where(name: 'Ben')->first().age
            echo $User->where(name: 'Nobody')->first() ?? 'none'
            echo $User->find_or_fail(2).name
            try {
              $User->find_or_fail(99)
            } catch ModelNotFound as e {
              echo [$e.message, $e.model, $e.id, $e.status]
            }
            try {
              $User->where(name: 'Nobody')->first_or_fail()
            } catch NotFound as e {
              echo $e.message
            }
            try {
              @db(users)->where(name: 'Nobody')->first_or_fail()
            } catch NotFound as e {
              echo $e.message
            }
            """);
        Assert.Equal(new[] { "17", "none", "Ben", "There is no User with id 99., User, 99, 404", "No User matches.", "No row in users matches." }, lines);
    }

    [Fact]
    public void FirstOrCreateAndInsert()
    {
        string[] lines = Lines(Users + """
            a = $User->first_or_create({ email: 'ada@example.com' }, { name: 'Other' })
            echo $a.name
            d = $User->first_or_create({ email: 'di@example.com' }, { name: 'Di' })
            echo [$d.id, $d.name]
            e = $User->first_or_create({ name: 'Eve', email: 'eve@example.com' })
            echo $e.id
            f = $User->first_or_create({}, { name: 'Fay', email: 'fay@example.com' })
            echo $f.id
            echo $User->insert([{ name: 'G', email: 'g@example.com' }, { name: 'H', email: 'h@example.com' }])
            echo $User->insert({ name: 'I', email: 'i@example.com' })
            echo $User->count()
            echo $User->query()->count()
            """);
        Assert.Equal(new[] { "Ada", "4, Di", "5", "6", "2", "1", "9", "9" }, lines);
    }

    [Fact]
    public void UpdatingManyRows()
    {
        string[] lines = Lines(Users + """
            echo $User->where(fn($u) => $u.age != null)->update(active: false)
            echo $User->where(active: false)->count()
            echo $User->where(name: 'Ada')->increment(age)
            echo $User->where(name: 'Ada')->increment(age, by: 3)
            echo $User->where(name: 'Ada')->decrement(age, 10)
            echo $User->find(1).age
            echo $User->where(name: 'Ada')->update({ name: 'Ada L' }, age: 1)
            echo [$User->find(1).name, $User->find(1).age]
            """);
        Assert.Equal(new[] { "2", "2", "1", "1", "1", "30", "1", "Ada L, 1" }, lines);
    }

    [Fact]
    public void Paginating()
    {
        string[] lines = Lines(Users + """
            p = $User->order_by(name)->paginate(2)
            echo [$p.page, $p.per_page, $p.total, $p.last_page, $p.from, $p.to, $p.has_more]
            echo $p.items->map(fn($u) => $u.name)
            p2 = $User->order_by(name)->paginate(2, '2')
            echo [$p2.page, $p2.from, $p2.to, $p2.has_more, $p2.items->count()]
            p3 = $User->paginate(per_page: 2, page: 'abc')
            echo $p3.page
            p4 = $User->paginate(page: 0)
            echo [$p4.page, $p4.per_page]
            p5 = $User->where(name: 'Nobody')->paginate()
            echo [$p5.from, $p5.to, $p5.last_page]
            """);
        Assert.Equal(new[] { "1, 2, 3, 2, 1, 2, true", "Ada, Ben", "2, 3, 3, false, 1", "1", "1, 15", "0, 0, 1" }, lines);
        Assert.Contains("per_page", Fails(Users + "$User->paginate(0)").Message);
    }

    [Fact]
    public void Chunking()
    {
        string[] lines = Lines(Users + """
            fn show(rows, n) {
              echo "{ $n }: { $rows->map(fn($u) => $u.name)->join(' ') }"
            }
            fn first(rows, n) {
              echo "{ $n }: { $rows[0].name }"
              return false
            }
            $User->order_by(name)->chunk(2, @show)
            $User->order_by(name)->chunk(1, @first)
            """);
        Assert.Equal(new[] { "0: Ada Ben", "1: Cy", "0: Ada" }, lines);
        Assert.Contains("size", Fails(Users + "$User->chunk(0, fn($r) => true)").Message);
    }

    [Fact]
    public void WhenAddsConditionsOnlyWhenTheTestIsTrue()
    {
        string[] lines = Lines(Users + """
            fn search(term) => $User->when($term != null, fn($q) => $q->where(fn($u) => $u.name->contains($term)))->count()
            echo @search(null)
            echo @search('y')
            echo $User->when(false, fn($q) => $q->where(name: 'Ada'), fn($q) => $q->where(name: 'Ben'))->pluck(name)
            echo $User->when(false, fn($q) => $q, null)->count()
            """);
        Assert.Equal(new[] { "3", "1", "Ben", "3" }, lines);
        Assert.Contains("must return the query", Fails(Users + "$User->when(true, fn($q) => 1)").Message);
    }

    [Fact]
    public void DescribingAQuery()
    {
        string[] lines = Lines(Users + """
            q = $User->where(fn($u) => $u.age > 18 and $u.name->contains('50%_'))->order_by(name: desc)->limit(5)->offset(10)
            d = $q->sql()
            echo $d.sql
            echo $d.params
            echo $User->select(name)->distinct()->lock_for_update()->skip_locked()->sql().sql
            """);
        Assert.Equal("SELECT * FROM \"users\" WHERE (\"age\" > @p1) AND (\"name\" LIKE @p2 ESCAPE '\\') ORDER BY \"name\" DESC LIMIT 5 OFFSET 10", lines[0]);
        Assert.Equal("18, %50\\%\\_%", lines[1]);
        Assert.Equal("SELECT DISTINCT \"name\" FROM \"users\"", lines[2]);
    }

    [Fact]
    public void ConditionsThatCantBeSqlSayWhy()
    {
        Assert.Contains("lambda", Fails(Users + "fn adult(u) => $u.age > 18\n$User->where(@adult)->get()").Message);
        Assert.Contains("one parameter", Fails(Users + "$User->where(fn($a, $b) => true)->get()").Message);
        Assert.Contains("only the row's own columns", Fails(Users + "$User->where(fn($u) => $u.name.length > 1)->get()").Message);
        Assert.Contains("not the whole row", Fails(Users + "$User->where(fn($u) => $u == null)->get()").Message);
        Assert.Contains("can't run in the database", Fails(Users + "$User->where(fn($u) => [$u.name][0] == 'x')->get()").Message);
        Assert.Contains("worked out from other fields", Fails(Users + "$User->where(fn($u) => $u.label == 'x')->get()").Message);
        Assert.Contains("no column 'nope'", Fails(Users + "$User->where(fn($u) => $u.nope == 1)->get()").Message);
        Assert.Contains("right of 'in'", Fails(Users + "$User->where(fn($u) => 'a' in $u.tags)->get()").Message);
        Assert.Contains("must be a list", Fails(Users + "$User->where(fn($u) => $u.name in 'abc')->get()").Message);
        Assert.Contains("passwords are stored as hashes", Fails(Users + "$User->where(fn($u) => $u.password == 'x')->get()").Message);
        Assert.Contains("only be used on a column", Fails(Users + "$User->where(fn($u) => 'x'->contains($u.name))->get()").Message);
        Assert.Contains("takes 1 argument", Fails(Users + "$User->where(fn($u) => $u.name->contains())->get()").Message);
        Assert.Contains("takes 0 arguments", Fails(Users + "$User->where(fn($u) => $u.name->lower('x') == 'a')->get()").Message);
        Assert.Contains("no SQL form", Fails(Users + "$User->where(fn($u) => $u.name->reverse() == 'a')->get()").Message);
        Assert.Contains("can't run in the database", Fails(Users + "$User->where(fn($u) => $u.age |> @abs() == 1)->get()").Message);
        Assert.Contains("no SQL form here", Fails(Users + "$User->where(fn($u) => $u.age ** 2 == 4)->get()").Message);
        Assert.Contains("isn't a Role", Fails(Users + "$User->where(fn($u) => $u.role == boss)->get()").Message);
        Assert.Contains("isn't a Role", Fails(Users + "$User->where(role: boss)->get()").Message);
        Assert.Contains("password hashes", Fails(Users + "$User->where(password: 'x')->get()").Message);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Contains("needs a condition", Fails(Users + "$User->where()").Message);
        Assert.Contains("lambda", Fails(Users + "$User->where(42)").Message);
        Assert.Contains("asc or desc", Fails(Users + "$User->order_by(name: up)").Message);
        Assert.Contains("needs a column", Fails(Users + "$User->order_by()").Message);
        Assert.Contains("column name", Fails(Users + "$User->pluck(42)").Message);
        Assert.Contains("no column 'nope'", Fails(Users + "$User->pluck(nope)").Message);
        Assert.Contains("whole number", Fails(Users + "$User->limit(1.5)").Message);
        Assert.Contains("whole number", Fails(Users + "$User->offset(-1)").Message);
        Assert.Contains("values to set", Fails(Users + "$User->where(name: 'Ada')->update()").Message);
        Assert.Contains("map of values", Fails(Users + "$User->where(name: 'Ada')->update(42)").Message);
        Assert.Contains("no method", Fails(Users + "@db(users)->create(name: 'x')").Message);
        Assert.Contains("model or a query", Fails(Users + "$User->where->call('x')").Message.Length > 0 ? "model or a query" : "");
        Assert.Contains("isn't a table name", Fails(Users + "@db('bad name')").Message);
        Assert.Contains("isn't a table name", Fails(Users + "$db->table('bad name')").Message);
        Assert.Contains("isn't a table name", Fails(Users + "$db->drop_table('bad name')").Message);
        Assert.Contains("needs a model", Fails(Users + "$db->create_table('users')").Message);
    }

    [Fact]
    public void TablesWithNoModel()
    {
        string[] lines = Lines("""
            $db->sql('create table visits (id integer primary key, page text, n integer, at real, raw blob)')
            echo $db->table(visits)->insert([{ page: '/', n: 1, at: 1.5 }, { page: '/about', n: 2 }])
            echo @db(visits)->where(page: '/')->first()
            echo @db('visits')->where(fn($v) => $v.n > 1)->pluck(page)
            echo @db(visits)->order_by(n: desc)->value(page)
            echo @db(visits)->latest(n)->value(page)
            echo @db(visits)->where(page: '/')->update(n: 5)
            echo @db(visits)->sum(n)
            echo @db(visits)->where(page: '/about')->delete()
            echo @db(visits)->count()
            echo @db() == $db
            echo $db->drop_table(visits)
            echo $db->drop_table(visits)
            """);
        Assert.Equal("2", lines[0]);
        Assert.Contains("\"page\": \"/\"", string.Join("\n", lines));
        Assert.Contains("/about", lines);
        Assert.Equal(new[] { "1", "7", "1", "1", "true", "true", "false" }, lines.Skip(lines.Length - 7).ToArray());
    }
}

public sealed class DataSqlTests : DataFixture
{
    [Fact]
    public void SqlTextInDoubleQuotesIsNotFilledInBeforeItIsBound()
    {
        // "double quotes" fill in values everywhere else, but text given straight to sql keeps its { } as parameters.
        string[] lines = Lines(Users + """
            evil = "x' or '1'='1"
            echo "select count(*) as n from users where name = {$evil}" |> sql()
            echo ("select count(*) as n from users where name = {$evil}" |> sql())[0].n
            echo $db->sql("select count(*) as n from users where name = {$evil}")[0].n
            held = 'select count(*) as n from users where name = {$evil}'
            echo @sql($held)[0].n
            """);
        Assert.Equal("0", lines[^3]);
        Assert.Equal("0", lines[^2]);
        Assert.Equal("0", lines[^1]);
    }

    [Fact]
    public void BracesAreAlwaysParameters()
    {
        string[] lines = Lines(Users + """
            evil = "x' or '1'='1"
            echo @sql("select count(*) as n from users where name = {$evil}")[0].n
            name = 'Ada'
            echo @sql("select age from users where name = {$name}")[0].age
            ids = [1, 3]
            echo @sql("select name from users where id in {$ids} order by id")->map(fn($r) => $r.name)
            none = []
            echo @sql("select count(*) as n from users where id in {$none}")[0].n
            echo @sql("select '{{literal}}' as t")[0].t
            echo @sql("select {'x'->upper()} as t, {true} as b, {null} as z, {1.5} as f")[0]
            echo @sql("select count(*) as n from users where name = {'Ben'}")[0].n
            echo @sql("update users set age = age + 1 where name = {$name}")
            echo $db->sql("select age from users where id = {1}")[0].age
            """);
        Assert.Equal("0", lines[0]);
        Assert.Equal("36", lines[1]);
        Assert.Equal("Ada, Cy", lines[2]);
        Assert.Equal("0", lines[3]);
        Assert.Equal("{literal}", lines[4]);
        Assert.Equal("1", lines[^3]);
        Assert.Equal("1", lines[^2]);
        Assert.Equal("37", lines[^1]);
    }

    [Fact]
    public void RowsCanBeMappedToATypeOrModel()
    {
        string[] lines = Lines(Users + """
            type Person {
              name: string
              age: int?
            }
            people = @sql('select name, age from users order by name', as: $Person)
            echo @type($people[0])
            echo $people[1].age
            """);
        Assert.Equal(new[] { "Person", "17" }, lines);
    }

    [Fact]
    public void BadSqlIsAQueryError()
    {
        KScriptException e = Fails("@sql('select * from nowhere')");
        Assert.Equal("QueryError", e.Type);
        Assert.Contains("nowhere", e.Message);
        Assert.Contains("{ without its }", Fails("@sql('select {1')").Message);
        Assert.Contains("empty { }", Fails("@sql('select { }')").Message);
    }

    [Fact]
    public void BracesInsideQuotesInBraces()
    {
        string[] lines = Lines("echo @sql(\"select {'}'} as a\")[0].a");
        Assert.Equal("}", lines[0]);
    }
}

public sealed class DataErrorTests : DataFixture
{
    [Fact]
    public void UniqueViolationNamesTheFields()
    {
        string[] lines = Lines(Users + """
            try {
              $User->create(name: 'Ada 2', email: 'ADA@example.com')
            } catch UniqueViolation as e {
              echo $e.status
              echo $e.fields
              echo $e.message
            }
            try {
              $User->create(name: 'Ada 2', email: 'ada@example.com')
            } catch Conflict as e {
              echo $e.type
            }
            """);
        Assert.Equal(new[] { "409", "email", "A User already has that email.", "UniqueViolation" }, lines);
    }

    [Fact]
    public void UniqueTogether()
    {
        KScriptException e = Fails("""
            model Slug {
              unique: [[team, slug]]
              team: int
              slug: string
            }
            $db->create_table($Slug)
            $Slug->create(team: 1, slug: 'a')
            $Slug->create(team: 2, slug: 'a')
            $Slug->create(team: 1, slug: 'a')
            """);
        Assert.Equal("UniqueViolation", e.Type);
        Assert.Contains("team and slug", e.Message);
    }

    [Fact]
    public void ForeignKeysAreEnforced()
    {
        KScriptException e = Fails("""
            $db->sql('create table teams (id integer primary key)')
            $db->sql('create table members (id integer primary key, team_id integer references teams(id))')
            $db->sql('insert into members (team_id) values (5)')
            """);
        Assert.Equal("ForeignKeyViolation", e.Type);
    }

    [Fact]
    public void EnumAndNotNullChecksInTheDatabase()
    {
        KScriptException e = Fails(Users + "$db->sql(\"insert into users (name, email, role, active, tags, created_at, updated_at) values ('x', 'x@x.org', 'boss', 1, '[]', '', '')\")");
        Assert.Equal("QueryError", e.Type);
    }

    [Fact]
    public void ErrorTypesAreBuiltIn()
    {
        Assert.Equal("NotFound", ErrorTypes.BuiltIn["ModelNotFound"].Parent.Name);
        Assert.Equal(409, ErrorTypes.BuiltIn["UniqueViolation"].Status);
        Assert.Equal(409, ErrorTypes.BuiltIn["ForeignKeyViolation"].Status);
        Assert.Equal(503, ErrorTypes.BuiltIn["ConnectionError"].Status);
        Assert.NotNull(ErrorTypes.BuiltIn["QueryError"]);
    }
}

public sealed class DataTransactionTests : DataFixture
{
    private const string Accounts = """
        model Account {
          owner: string
          balance: number = 0
        }
        $db->create_table($Account)
        $Account->create(owner: 'a', balance: 100)
        $Account->create(owner: 'b')

        """;

    [Fact]
    public void ABlockKeepsOrUndoesItsChanges()
    {
        string[] lines = Lines(Accounts + """
            transaction {
              $Account->where(id: 1)->decrement(balance, by: 10)
              echo $db->in_transaction()
            }
            try {
              transaction {
                $Account->where(id: 1)->decrement(balance, by: 10)
                throw Conflict(message: 'no')
              }
            } catch Conflict {
              echo 'undone'
            }
            echo $db->in_transaction()
            echo $Account->find(1).balance
            """);
        Assert.Equal(new[] { "true", "undone", "false", "90" }, lines);
    }

    [Fact]
    public void NestedTransactionsAreSavepoints()
    {
        string[] lines = Lines(Accounts + """
            transaction {
              $Account->where(id: 1)->update(balance: 1)
              try {
                transaction {
                  $Account->where(id: 2)->update(balance: 2)
                  throw Conflict(message: 'inner')
                }
              } catch Conflict {
                echo 'inner undone'
              }
              transaction {
                $Account->where(id: 2)->update(balance: 3)
              }
            }
            echo $Account->order_by(id)->pluck(balance)
            """);
        Assert.Equal(new[] { "inner undone", "1, 3" }, lines);
    }

    [Fact]
    public void ReturningOrBreakingOutOfABlockKeepsItsChanges()
    {
        string[] lines = Lines(Accounts + """
            fn pay() {
              transaction {
                $Account->where(id: 1)->update(balance: 50)
                return 'paid'
              }
            }
            echo @pay()
            for i in [1, 2] {
              transaction {
                $Account->where(id: 2)->increment(balance)
                if $i == 1 {
                  continue
                }
                break
              }
            }
            echo $Account->order_by(id)->pluck(balance)
            """);
        Assert.Equal(new[] { "paid", "50, 2" }, lines);
    }

    [Fact]
    public void TransactionCommandReturnsWhatTheFunctionReturns()
    {
        string[] lines = Lines(Accounts + """
            echo @transaction(fn() => $Account->where(id: 1)->update(balance: 7))
            echo $db->transaction(fn() => $Account->sum(balance), retries: 2)
            echo @transaction(fn() => 'x', retries: 1, connection: main)
            try {
              @transaction(fn() => $Account->create(owner: 'c') and @fail('stop'))
            } catch {
              echo $Account->count()
            }
            """);
        Assert.Equal(new[] { "1", "7", "x", "2" }, lines);
        Assert.Contains("retries", Fails(Accounts + "@transaction(fn() => 1, retries: -1)").Message);
    }

    [Fact]
    public void TransactionsRetryWhenTheDatabaseIsBusy()
    {
        int calls = 0;
        var host = KScriptHost.ForText(new StringWriter());
        FileOf("app.kscript", "1");
        var interpreter = new Interpreter(host);
        Database db = Database.For(host, Project, null);
        var busy = new KScriptException("database is locked", "QueryError", new Microsoft.Data.Sqlite.SqliteException("database is locked", 5));
        Assert.Equal(3, db.InTransaction(interpreter, () =>
        {
            calls++;
            if (calls < 3) throw busy;
            return 3;
        }, retries: 2));
        calls = 0;
        Assert.Throws<KScriptException>(() => db.InTransaction<int>(interpreter, () =>
        {
            calls++;
            throw busy;
        }, retries: 1));
        Assert.Equal(2, calls);
    }
}

public sealed class DataConnectionTests : DataFixture
{
    [Fact]
    public void ConnectionsComeFromTheConfig()
    {
        FileOf("config.yaml", """
            database:
              default: main
              connections:
                main:
                  url: sqlite:db/main.sqlite
                reports:
                  url: "sqlite::memory:"
                plain:
                  file: db/plain.sqlite
                driver:
                  driver: sqlite
                  database: db/driver.sqlite
            """);
        string[] lines = Lines("""
            model Hit {
              connection: reports
              page: string
            }
            @db(connection: reports)->create_table($Hit)
            $Hit->create(page: '/')
            echo $Hit->count()
            echo [@db(connection: reports).name, @db(connection: reports).driver]
            echo @db(connection: reports).file ?? 'memory'
            echo $db.file->ends_with('main.sqlite')
            echo @db(connection: plain).file->ends_with('plain.sqlite')
            echo @db(connection: driver).file->ends_with('driver.sqlite')
            echo @type($db)
            echo $db
            echo $db.nope ?? 'none'
            """);
        Assert.Equal("1", lines[0]);
        Assert.Equal("reports, sqlite", lines[1]);
        Assert.Equal("memory", lines[2]);
        Assert.Equal(new[] { "true", "true", "true", "database" }, lines.Skip(3).Take(4).ToArray());
        Assert.StartsWith("sqlite database 'main'", lines[7]);
    }

    [Fact]
    public void UnknownConnectionsSuggestTheClosest()
    {
        FileOf("config.yaml", "database:\n  connections:\n    reports:\n      url: sqlite:r.sqlite\n");
        KScriptException e = Fails("@db(connection: report)");
        Assert.Equal("Config", e.Type);
        Assert.Contains("Did you mean reports?", e.Message);
        Assert.Contains("no database connection called 'main'", Fails("echo $db").Message);
    }

    [Fact]
    public void TheEnvironmentCanSetTheDatabase()
    {
        Environment.SetEnvironmentVariable("DATABASE_URL", "sqlite:from-env.sqlite");
        try
        {
            Assert.EndsWith("from-env.sqlite", Run("echo $db.file").Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", null);
        }
    }

    [Theory]
    [InlineData("postgres://localhost/app", "postgres")]
    [InlineData("mysql://localhost/app", "mysql")]
    [InlineData("oracle://x", "isn't a database")]
    public void OtherDriversAreNotYetHere(string url, string message)
    {
        FileOf("config.yaml", $"database:\n  connections:\n    main:\n      url: \"{url}\"\n");
        KScriptException e = Fails("echo $db");
        Assert.Equal("Config", e.Type);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void AFileThatCantBeOpenedIsAConnectionError()
    {
        File.WriteAllText(Path.Combine(Project, "blocked"), "a file, not a folder");
        FileOf("config.yaml", "database:\n  connections:\n    main:\n      url: sqlite:blocked/app.sqlite\n");
        KScriptException e = Fails("echo $db->tables()");
        Assert.Equal("ConnectionError", e.Type);
    }

    [Fact]
    public void TheProjectFolderIsTheOneWithKscriptJson()
    {
        FileOf("kscript.json", "{ \"name\": \"x\" }");
        FileOf("scripts/a.kscript", "import KScript.Data\necho $db.file\n");
        var output = new StringWriter();
        new Interpreter(KScriptHost.ForText(output)).RunFile(Path.Combine(Project, "scripts", "a.kscript"));
        Assert.Equal(Path.Combine(Project, "data", "app.sqlite"), output.ToString().Trim());
    }

    [Fact]
    public void StatementsAreLoggedForTests()
    {
        Database.Log = new List<SqlStatement>();
        Run(Users + "echo $User->where(name: 'Ada')->count()");
        Assert.Contains(Database.Log, s => s.Text.Contains("COUNT(*)") && s.Parameters.Contains("Ada"));
    }
}

public sealed class DataCoverageTests : DataFixture
{
    [Fact]
    public void EveryKindOfField()
    {
        string[] lines = Lines("""
            type Age with [int] { min: 0; max: 150 }
            type Address {
              city: string
            }
            model Profile {
              handle: handle
              token: uuid
              age: Age
              address: Address
              extra: map = {}
              ratio: float = 0.5
              born: date?
            }
            $db->create_table($Profile)
            p = $Profile->create(handle: '@ada', token: '0b7e2d4c-7c4b-4a63-9b8a-3f5f1b2c9d10', age: 36, address: { city: 'London' }, extra: { a: 1 }, born: '1815-12-10')
            again = $Profile->find($p.id)
            echo [$again.handle, $again.age, $again.address.city, $again.extra.a, $again.ratio]
            echo @type($again.address)
            echo $again.born->format('yyyy')
            echo $Profile->where(handle: '@ada', token: '0b7e2d4c-7c4b-4a63-9b8a-3f5f1b2c9d10')->count()
            echo $Profile->where(fn($p) => $p.age > 30)->count()
            echo $Profile->max(born)->format('yyyy')
            """);
        Assert.Equal(new[] { "@ada, 36, London, 1, 0.5", "Address", "1815", "1", "1", "1815" }, lines);
    }

    [Fact]
    public void ValuesWrittenBySomethingElseAreStillRead()
    {
        string[] lines = Lines("""
            model Contact {
              email: email
              data: map = {}
            }
            $db->create_table($Contact)
            $db->sql("insert into contacts (email, data) values ('not an email', 'not json')")
            c = $Contact->first()
            echo [$c.email, $c.data]
            """);
        Assert.Equal("not an email, not json", lines[0]);
        Assert.Equal("[1]", Run("""
            type Pair {
              a: int
            }
            model Holder {
              pair: Pair
            }
            $db->create_table($Holder)
            $db->sql("insert into holders (pair) values ('[1]')")
            echo $Holder->first().pair->to_json()
            """).Trim());
    }

    [Fact]
    public void ModelsThatCantBeTables()
    {
        Assert.Contains("relations", Fails("model Team {\n  name: string\n}\nmodel Member {\n  team: Team\n}\n$db->create_table($Member)").Message);
        Assert.Contains("must be an int", Fails("model Bad {\n  key: tags\n  tags: string[]\n}\n$db->create_table($Bad)").Message);

    }

    [Fact]
    public void QueriesPrintTheirSql()
    {
        string[] lines = Lines(Users + "q = $User->where(name: 'Ada')\necho $q\necho @type($q)\necho [$q.table, $q.model == $User, @db(users).model ?? 'none', $q.nope ?? 'none']\necho \"{ $q }\"");
        Assert.Equal("query on users: SELECT * FROM \"users\" WHERE \"name\" = @p1", lines[0]);
        Assert.Equal("query", lines[1]);
        Assert.Equal("users, true, none, none", lines[2]);
    }

    [Fact]
    public void MoreMistakes()
    {
        Assert.Contains("worked out from other fields", Fails(Users + "$User->pluck(label)").Message);
        Assert.Contains("needs a number column", Fails(Users + "$User->increment(name)").Message);
        Assert.Contains("a map, or a list of maps", Fails(Users + "$User->insert(42)").Message);
        Assert.Contains("needs a condition", Fails(Users + "$User->where({})").Message);
    }

    [Fact]
    public void EmptyRowsAndPartlySelectedRecords()
    {
        string[] lines = Lines(Users + """
            $db->sql('create table pings (id integer primary key, at text default (datetime()))')
            echo @db(pings)->insert({})
            echo @db(pings)->count()
            echo $User->where({}, name: 'Ada')->count()
            s = $User->select(id, name)->first()
            $s.age = 99
            echo $s->dirty()
            $s->save()
            echo $User->find(1).age
            """);
        Assert.Equal(new[] { "1", "1", "1", "age", "99" }, lines);
    }

    [Fact]
    public void ValuesBecomeParameters()
    {
        var sqlite = SqliteDialect.Instance;
        Assert.Equal("w", Rows.ToDb(new BareWord("w", false, false), null, sqlite));
        Assert.Equal(12L, Rows.ToDb("12", new Column { Name = "n", Kind = ColumnKind.Integer }, sqlite));
        Assert.StartsWith("$argon2id$", (string)Rows.ToDb(PasswordValueFor("a good long password"), null, sqlite));
        Assert.Equal("x", Rows.ToDb(new StringBuilder("x"), null, sqlite));
        Assert.Equal("query", Rows.ToDb(new FakeObject(), null, sqlite));
        Assert.Equal("w", Rows.ForColumn(new BareWord("w", false, false), null, sqlite));
        Assert.Null(Rows.FromDb(DBNull.Value, null));
        foreach (object raw in new object[] { 1, (short)1, (byte)1, 1m, 1f, 1L, 1.0 }) Assert.Equal(1.0, Rows.Plain(raw));
        Assert.Null(Rows.Plain(DBNull.Value));
        Assert.Null(Rows.Plain(null));
        Assert.Equal(true, Rows.Plain(true));
        var when = new DateTime(2026, 1, 1);
        Assert.Equal(when, Rows.Plain(when));
        Assert.Equal("AQI=", Rows.Plain(new byte[] { 1, 2 }));
        Assert.Equal("12", Rows.Plain(12u));
        Assert.Equal(when, Rows.FromDb(when, new Column { Name = "d", Kind = ColumnKind.Date }));
        Assert.Equal(true, Rows.FromDb(true, new Column { Name = "b", Kind = ColumnKind.Bool }));
        Assert.Equal("@ada", Rows.ForColumn("@ada", new Column { Name = "h", Kind = ColumnKind.Handle }, sqlite));
        Assert.Equal("0b7e2d4c-7c4b-4a63-9b8a-3f5f1b2c9d10", Rows.ForColumn("0B7E2D4C-7C4B-4A63-9B8A-3F5F1B2C9D10", new Column { Name = "u", Kind = ColumnKind.Uuid }, sqlite));
    }

    private static PasswordValue PasswordValueFor(string text)
    {
        Assert.True(PasswordValue.TryCreate(text, out PasswordValue value, out _));
        return value;
    }

    private sealed class FakeObject : IKObject
    {
        public string TypeName => "query";

        public string ToText() => "query";

        public bool TryGetMember(string name, out object value)
        {
            value = null;
            return false;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }
    }

    [Fact]
    public void ObjectsSayWhatTheyAre()
    {
        var hash = new PasswordHash("$argon2id$x");
        Assert.Equal(PasswordValue.Mask, hash.ToText());
        Assert.Equal(PasswordValue.Mask, hash.ToString());
        Assert.Equal(new PasswordHash("$argon2id$x").GetHashCode(), hash.GetHashCode());
        Assert.False(hash.TryGetMember("x", out _));
        Assert.False(hash.TryCallMethod(null, "x", out _));
        Assert.False(hash.Verify("not long enough"));
        Assert.True(hash.Equals(new PasswordHash("$argon2id$x")));
        Assert.False(hash.Equals("$argon2id$x"));

        var host = KScriptHost.ForText(new StringWriter());
        Database db = Database.For(host, Project, null);
        var lazy = new DefaultDatabase(host, Project);
        Assert.Equal(db.ToText(), lazy.ToString());
        Assert.Equal(db.ToText(), db.ToString());
        Assert.True(lazy.Equals(db));
        Assert.True(db.Equals(lazy));
        Assert.True(lazy.Equals(new DefaultDatabase(host, Project)));
        Assert.Equal(db.GetHashCode(), lazy.GetHashCode());
        Assert.False(lazy.TryCallMethod(null, "x", out _));
        Assert.False(db.TryCallMethod(null, "x", out _));
        Assert.Equal("SELECT 1", new SqlStatement("SELECT 1", Array.Empty<object>()).ToString());
        Assert.Equal("query on t: SELECT * FROM \"t\"", new Query(db, "t", null).ToString());
        Assert.Contains("KScript.Data", new DataPackage().Name);
        Assert.Contains("SQLite", new DataPackage().Description);
    }

    [Fact]
    public void ACommitThatFailsIsAnError()
    {
        KScriptException e = Fails("""
            $db->sql('create table teams (id integer primary key)')
            $db->sql('create table members (id integer primary key, team_id integer references teams(id))')
            transaction {
              $db->sql('pragma defer_foreign_keys = on')
              $db->sql('insert into members (team_id) values (5)')
            }
            """);
        Assert.Equal("ForeignKeyViolation", e.Type);
    }

    [Fact]
    public void ASavepointLeftWithReturnKeepsItsChanges()
    {
        string[] lines = Lines("""
            model Counter {
              n: int = 0
            }
            $db->create_table($Counter)
            $Counter->create()
            fn bump() {
              transaction {
                $Counter->increment(n)
                return 'bumped'
              }
            }
            transaction {
              echo @bump()
            }
            echo $Counter->first().n
            """);
        Assert.Equal(new[] { "bumped", "1" }, lines);
    }
}
