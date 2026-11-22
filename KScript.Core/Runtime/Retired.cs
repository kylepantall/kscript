using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// KScript 1 elements, attributes and commands that KScript 2 removed, with what to write instead. The runtime
    /// stops with this message when a script uses one, and the language server shows it with a quick fix.
    /// </summary>
    public static class Retired
    {
        public static readonly IReadOnlyDictionary<string, RetiredInfo> Elements = Table(
            ("def", "Use <let name=\"...\" value=\"...\" /> instead.", "let"),
            ("update", "Use <set name=\"...\" value=\"...\" />.", "set"),
            ("updateContent", "Use <set name=\"...\" value=\"...\" />.", "set"),
            ("temp", "Use <let> inside the block that needs it; KScript 2 variables are scoped.", "let"),
            ("defm", "Use <let name=\"...\"> with a map literal { key: value }.", "let"),
            ("guard", "Use <const name=\"...\" value=\"...\" />.", null),
            ("array", "Use <let name=\"...\" value=\"['a', 'b']\" /> with a list literal.", null),
            ("aecho", "Use <echo>{ $list->join(', ') }</echo>.", null),
            ("ainsert", "Use <set name=\"list\" value=\"$list->push($item)\" />.", null),
            ("aremove", "Use <set name=\"list\" value=\"$list->remove_at($index)\" />.", null),
            ("minsert", "Use <set name=\"map\" value=\"$map->set($key, $value)\" />, or <do>map.key = $value</do>.", null),
            ("afor", "Use <for each=\"item\" in=\"$list\">.", "for"),
            ("forxy", "Use <for each=\"i\" in=\"0..10\">.", "for"),
            ("dowhile", "Use <while test=\"...\">.", "while"),
            ("forever", "Use <while test=\"true\">.", "while"),
            ("func", "Use <match value=\"...\"> with <case is=\"...\">.", "match"),
            ("math", "Write the arithmetic in an expression: <set name=\"x\" value=\"$a + $b\" />.", null),
            ("method", "Use <fn name=\"...\" params=\"...\"> and call it as @name().", "fn"),
            ("run", "Use <fn> and @name(), or <call fn=\"...\">.", null),
            ("thread", "Use <spawn fn=\"...\" />.", "spawn"),
            ("onexception", "Use <try> with <catch as=\"err\">.", null),
            ("load", "Use <import path=\"...\" />.", "import"),
            ("ref", "Loading plug-in assemblies isn't part of KScript 2; import a package with <import package=\"...\" />.", null),
            ("group", "Use <fn> to group related steps.", null),
            ("x", "Not part of KScript 2.", null),
            ("auto", "Not part of KScript 2; build sentences with { } interpolation.", null),
            ("languages", "Use a map: <let name=\"text\" value=\"{ en: 'Hello', fr: 'Bonjour' }\" />.", null),
            ("watcher", "Not part of KScript 2; check the value with <if> where it changes.", null),
            ("watch", "Not part of KScript 2; check the value with <if> where it changes.", null),
            ("rule", "Not part of KScript 2; check the value with <if> where it changes.", null),
            ("loadHtml", "Use kscript serve with HTML pages (KScript.Web).", null),
            ("ginput", "Use <input into=\"...\"> from KScript.Terminal.", "input"),
            ("end", "Use <exit /> (or <exit code=\"1\" />).", "exit"),
            ("anykey", "Use <pause /> from KScript.Terminal.", "pause"),
            ("new_dir", "Use @create_folder(path) from KScript.IO.", null),
            ("file_info", "Use @file(path), which returns a file value with .size, .modified and more (KScript.IO).", null),
            ("registry", "The Windows registry isn't part of KScript 2.", null),
            // One form per action: these statements became commands (or were a second spelling of one).
            ("write", "Use @write_text(path, text), @append_text(path, text) or @write_json(path, value) from KScript.IO.", null),
            ("move", "Use @move(from, to) from KScript.IO.", null),
            ("copy", "Use @copy(from, to) from KScript.IO.", null),
            ("delete", "Use @delete(path) from KScript.IO.", null),
            ("mkdir", "Use @create_folder(path) from KScript.IO.", null),
            ("assert", "Use @assert(condition, message?) from KScript.Test.", null),
            ("stylesheet", "Use @stylesheet(path, vars?) from KScript.Style, which returns the CSS: @write_text('theme.css', @stylesheet('theme.ks.css')).", null),
            ("dispatch", "Call the job's own method: SendWelcome(user_id: 7)->dispatch(), with delay:, at:, queue: or connection: when needed (KScript.Queue).", null),
            ("download", "Use @download(url, to) from KScript.Net, which returns the path of the file it saved.", null),
            ("redirect", "In a page, use <respond redirect=\"/thanks\" />. In a controller, return @redirect('/thanks') (KScript.Http).", null));

        public static readonly IReadOnlyDictionary<string, RetiredInfo> Attributes = Table(
            ("version", "There is only one KScript now; the version attribute isn't needed.", null),
            ("strict", "Strict checking is always on in KScript 2.", null),
            ("dynamic_defs", "Strict checking is always on in KScript 2; declare variables with <let>.", null),
            ("checkfor", "Write the condition with not: test=\"not $x\".", null),
            ("condition", "Use test=\"...\" with a KScript 2 expression.", "test"),
            ("method", "Use fn=\"...\" with a <fn> function.", "fn"),
            ("_insertion", "Closures on arrays were removed in KScript 2. Use list methods with fn() lambdas, such as $list->map(fn(x) => ...).", null),
            ("_removal", "Closures on arrays were removed in KScript 2. Use list methods with fn() lambdas, such as $list->map(fn(x) => ...).", null),
            // Lengths of time are durations with a unit (docs/contributing.md).
            ("ms", "Give the length of time with its unit: duration=\"500ms\".", null),
            ("seconds", "Give the length of time with its unit: duration=\"2s\".", null),
            // Tables take the same arguments in a terminal and in a document.
            ("data", "A table's rows are given as rows.", "rows"),
            ("headers", "Give the headings with the columns: columns=\"{ Name: 'name', Age: 'age' }\".", null));

        public static readonly IReadOnlyDictionary<string, RetiredInfo> Commands = Table(
            ("math", "Write the arithmetic directly: $total + $price * $qty.", null),
            ("math_previous", "Write the arithmetic directly and store it with <let> or <set>.", null),
            ("more_less", "Use the comparison operators: $a < $b, <=, >, >= (in an XML attribute: lt, le, gt, ge).", null),
            ("equals", "Use == (loose) or === (strict).", null),
            ("iftrue", "Use the conditional operator: $test ? 'yes' : 'no'.", null),
            ("if", "Use the conditional operator: $test ? 'yes' : 'no'.", null),
            ("ifnull", "Use ??: $value ?? 'fallback'.", null),
            ("null", "Compare with null: $x == null, or use $x ?? fallback.", null),
            ("and", "Use the and operator: $a and $b.", null),
            ("any", "Use the or operator: $a or $b.", null),
            ("not", "Use the not operator: not $a.", null),
            ("acount", "Use ->count(): $list->count().", null),
            ("array", "Index the list directly: $list[1].", null),
            ("atindex", "Index the list directly: $list[1].", null),
            ("values", "Index the map directly: $map.key or $map['key'].", null),
            ("tie", "Use a list or map literal.", null),
            ("constant", "Use <const name=\"...\" value=\"...\" />.", null),
            ("defstate", "Variable history is not part of KScript 2.", null),
            ("parse", "Values are never re-read as script in KScript 2; use { } interpolation.", null),
            ("pretty_print", "Use $value->to_json(true).", null),
            ("types", "Not part of KScript 2.", null),
            ("this", "Not part of KScript 2.", null),
            ("parent", "Not part of KScript 2.", null),
            ("getfiles", "Use @files(folder), which returns a list of file values (KScript.IO).", "files"),
            ("find_file", "Use @files(folder)->find(fn(f) => ...) (KScript.IO).", null),
            ("dir_exists", "Use @is_folder(path) (KScript.IO).", "is_folder"),
            ("extension_of", "Use $file.extension or @file(path).extension (KScript.IO).", null),
            ("path", "Use @file(path) and its methods: .name, .directory, .extension (KScript.IO).", "file"),
            ("directory", "Use @known_folder('desktop') or @cwd() (KScript.IO).", "known_folder"),
            ("k", "Use $os, $script and $env: $os.name, $script.path, $env.HOME.", null),
            ("args", "Use the $args list: $args[0].", null),
            ("length", "Use ->count().", null),
            ("contains", "Use ->contains(): $text->contains('x').", null),
            ("split", "Use ->split(separator), which returns a list.", null),
            ("time", "Use @now()->format('HH:mm').", null),
            ("datetime", "Use @now(), or @now()->format('yyyy-MM-dd HH:mm').", "now"),
            ("to_date", "Use ->to_date() on text, then ->add_days() and friends.", null),
            ("char", "Use @char_of(code).", "char_of"),
            ("average", "Use $list->average(), or @average() from KScript.Math.", null),
            ("from_base64", "Use ->from_base64().", null),
            ("to_base64", "Use ->to_base64().", null),
            ("to_md5", "Use ->md5().", null),
            ("xpath", "XPath isn't part of KScript 2; read XML with @read_text() and string methods, or use JSON.", null),
            ("registry", "The Windows registry isn't part of KScript 2.", null),
            // One name per thing: commands that only repeated a method, a statement or an operator.
            ("to_upper", "Use ->upper(): $text->upper().", null),
            ("to_lower", "Use ->lower(): $text->lower().", null),
            ("string", "Use ->to_string(): $value->to_string().", null),
            ("number", "Use ->to_number(): $text->to_number(), or ->to_number(0) for a default.", null),
            ("json", "Use ->json(): $text->json(), or ->json(as: Repo).", null),
            ("to_json", "Use ->to_json(): $value->to_json().", null),
            ("yaml", "Use ->yaml(): $text->yaml().", null),
            ("to_yaml", "Use ->to_yaml(): $value->to_yaml().", null),
            ("guid", "Use @uuid().", "uuid"),
            ("date", "Use ->to_date(): '2026-11-19'->to_date().", null),
            ("type", "Use ->type(): $value->type().", null),
            ("escape", "Use ->escape(): $text->escape().", null),
            ("base64", "Use ->to_base64(): $text->to_base64().", null),
            ("url_encode", "Use ->url_encode(): $text->url_encode().", null),
            ("url_decode", "Use ->url_decode(): $text->url_decode().", null),
            ("pow", "Use the ** operator: $base ** $exponent.", null),
            ("contrast", "Use @contrast_color(color, dark?, light?) (KScript.Style).", "contrast_color"),
            ("exit", "Use the exit statement: exit 1.", null),
            ("error", "Use the throw statement: throw Validation \"Too short\".", null),
            ("transaction", "Use the transaction statement around the work, or $db->transaction(fn) when you need its result (KScript.Data).", null),
            // Renamed so the standard packages follow one set of conventions (docs/contributing.md).
            ("config", "Read a setting as $config.database.port, or $config->get('database.port', 5432) for a path held in a variable or a default.", null),
            ("signed_in", "Use @is_signed_in() (KScript.Auth).", "is_signed_in"),
            // log is the statement that writes a log entry, so the logarithm has its own name.
            ("log", "The logarithm was renamed to @logarithm(number, base?) (KScript.Math): @logarithm(8, 2).", "logarithm"));

        /// <summary>
        /// Elements whose name changed so that every statement name is one word, snake_case or PascalCase, with no
        /// hyphen and no abbreviation. The old name is an error that says the new one; it is not an alias.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, RetiredInfo> RenamedElements = new[]
        {
            ("before-each", "before_each"),
            ("after-each", "after_each"),
            ("expect-error", "expect_error"),
            ("log-provider", "log_provider"),
            ("css-text", "css_text"),
            ("marray", "collection"),
        }.ToDictionary(r => r.Item1, r => new RetiredInfo(r.Item1, $"is now {r.Item2}.", r.Item2) { Renamed = true }, StringComparer.Ordinal);

        /// <summary>
        /// Arguments whose name changed, by element: the variable a result goes into is <c>into</c> (<c>to</c> is
        /// only ever a destination), the file an element reads is <c>path</c>, and a position is <c>at</c> as in
        /// <c>for item at i</c>. An element of <c>*</c> means every element that has the new name. Names that only
        /// swapped a hyphen for an underscore (<c>max-tokens</c>) need no row: see <see cref="Attribute"/>.
        /// </summary>
        public static readonly (string Element, string Old, string New, string Instead)[] RenamedAttributes =
        {
            ("input", "to", "into", null),
            ("picker", "to", "into", null),
            ("menu", "to", "into", null),
            ("confirm", "to", "into", null),
            ("llm", "to", "into", null),
            ("capture", "to", "into", null),
            ("config", "to", "into", null),
            ("call", "to", "into", null),
            ("menu", "index", "at", null),
            ("for", "index", "at", null),
            ("import", "src", "path", null),
            ("include", "src", "path", null),
            ("layout", "src", "path", null),
            ("Markdown", "src", "path", null),
            ("Figure", "src", "path", null),
            ("Bibliography", "src", "path", null),
            ("type", "sample", "path", null),
            ("log_provider", "colour", "color", null),
            ("test", "each", null, TestInLoop),
            ("test", "in", null, TestInLoop),
            ("test", "index", null, TestInLoop),
        };

        private const string TestInLoop = "was replaced by a for loop round the test, the same loop as anywhere else: for n at i in [1, 2, 3] { test(name: \"doubles {$n}\") { } } (in XML, <for each=\"n\" in=\"[1, 2, 3]\"><test name=\"doubles {$n}\">...</test></for>).";

        /// <summary>A renamed element by its old name, or null.</summary>
        public static RetiredInfo RenamedElement(string name) =>
            name != null && RenamedElements.TryGetValue(name, out RetiredInfo r) ? r : null;

        /// <summary>
        /// An argument of <paramref name="element"/> written by a name it no longer has: a renamed one (see
        /// <see cref="RenamedAttributes"/>), or a hyphenated spelling of one it has (<c>must-exist</c> for
        /// <c>must_exist</c>). Null when the name was never one of the element's.
        /// </summary>
        public static RetiredInfo RenamedAttribute(ElementInfo element, string name)
        {
            if (element == null || name == null || element.GetAttribute(name) != null) return null;
            foreach (var row in RenamedAttributes)
            {
                if (row.Old != name || row.Element != element.Name) continue;
                if (row.New == null) return new RetiredInfo(name, row.Instead) { Renamed = true };
                if (element.GetAttribute(row.New) != null) return new RetiredInfo(name, $"is now {row.New}.", row.New) { Renamed = true };
            }
            if (name.Contains('-') && element.GetAttribute(name.Replace('-', '_')) is AttributeInfo snake)
                return new RetiredInfo(name, $"is now {snake.Name}: argument names use _ and never a hyphen.", snake.Name) { Renamed = true };
            return null;
        }

        // A plain loop: every package load reads these tables (Conventions), and LINQ over tuples is slow to start.
        private static IReadOnlyDictionary<string, RetiredInfo> Table(params (string Name, string Replacement, string Rename)[] rows)
        {
            var table = new Dictionary<string, RetiredInfo>(rows.Length, StringComparer.Ordinal);
            foreach (var r in rows) table.Add(r.Name, new RetiredInfo(r.Name, r.Replacement, r.Rename));
            return table;
        }
    }
}
