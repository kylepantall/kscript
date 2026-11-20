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
            ("guard", "Use <let ... const=\"yes\">.", null),
            ("array", "Use <let name=\"...\" value=\"['a', 'b']\" /> with a list literal.", null),
            ("aecho", "Use <echo>{ $list->join(', ') }</echo>.", null),
            ("ainsert", "Use <set name=\"list\" value=\"$list->push($item)\" />.", null),
            ("aremove", "Use <set name=\"list\" value=\"$list->remove_at($index)\" />.", null),
            ("minsert", "Use <set name=\"map\" value=\"$map->set($key, $value)\" />, or <do>$map.key = $value</do>.", null),
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
            ("load", "Use <import src=\"...\" />.", "import"),
            ("ref", "Loading plug-in assemblies isn't part of KScript 2; import a package with <import package=\"...\" />.", null),
            ("group", "Use <fn> to group related steps.", null),
            ("x", "Not part of KScript 2.", null),
            ("auto", "Not part of KScript 2; build sentences with { } interpolation.", null),
            ("languages", "Use a map: <let name=\"text\" value=\"{ en: 'Hello', fr: 'Bonjour' }\" />.", null),
            ("watcher", "Not part of KScript 2; check the value with <if> where it changes.", null),
            ("watch", "Not part of KScript 2; check the value with <if> where it changes.", null),
            ("rule", "Not part of KScript 2; check the value with <if> where it changes.", null),
            ("loadHtml", "Use kscript serve with HTML pages (KScript.Web).", null),
            ("ginput", "Use <input to=\"...\"> from KScript.Terminal.", "input"),
            ("end", "Use <exit /> (or <exit code=\"1\" />).", "exit"),
            ("anykey", "Use <pause /> from KScript.Terminal.", "pause"),
            ("new_dir", "Use <mkdir path=\"...\" /> from KScript.IO.", "mkdir"),
            ("file_info", "Use @file(path), which returns a file value with ->size(), ->modified() and more (KScript.IO).", null),
            ("registry", "The Windows registry isn't part of KScript 2.", null));

        public static readonly IReadOnlyDictionary<string, RetiredInfo> Attributes = Table(
            ("version", "There is only one KScript now; the version attribute isn't needed.", null),
            ("strict", "Strict checking is always on in KScript 2.", null),
            ("dynamic_defs", "Strict checking is always on in KScript 2; declare variables with <let>.", null),
            ("checkfor", "Write the condition with not: test=\"not $x\".", null),
            ("condition", "Use test=\"...\" with a KScript 2 expression.", "test"),
            ("method", "Use fn=\"...\" with a <fn> function.", "fn"),
            ("_insertion", "Closures on arrays were removed in KScript 2. Use list methods with fn() lambdas, such as $list->map(fn($x) => ...).", null),
            ("_removal", "Closures on arrays were removed in KScript 2. Use list methods with fn() lambdas, such as $list->map(fn($x) => ...).", null));

        public static readonly IReadOnlyDictionary<string, RetiredInfo> Commands = Table(
            ("math", "Write the arithmetic directly: $total + $price * $qty.", null),
            ("math_previous", "Write the arithmetic directly and store it with <let> or <set>.", null),
            ("more_less", "Use the comparison operators: $a lt $b, le, gt, ge.", null),
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
            ("constant", "Use <let name=\"...\" const=\"yes\">.", null),
            ("defstate", "Variable history is not part of KScript 2.", null),
            ("parse", "Values are never re-read as script in KScript 2; use { } interpolation.", null),
            ("pretty_print", "Use $value->to_json(true).", null),
            ("types", "Not part of KScript 2.", null),
            ("this", "Not part of KScript 2.", null),
            ("parent", "Not part of KScript 2.", null),
            ("getfiles", "Use @files(folder), which returns a list of file values (KScript.IO).", "files"),
            ("find_file", "Use @files(folder)->find(fn($f) => ...) (KScript.IO).", null),
            ("dir_exists", "Use @is_folder(path) (KScript.IO).", "is_folder"),
            ("extension_of", "Use $file->extension() or @file(path)->extension() (KScript.IO).", null),
            ("path", "Use @file(path) and its methods: ->name(), ->directory(), ->extension() (KScript.IO).", "file"),
            ("directory", "Use @known_folder('desktop') or @cwd() (KScript.IO).", "known_folder"),
            ("api", "Use @fetch(url)->text() (KScript.Net).", "fetch"),
            ("k", "Use $os, $script and $env: $os.name, $script.path, $env.HOME.", null),
            ("args", "Use the $args list: $args[0].", null),
            ("length", "Use ->length() (or ->count()).", null),
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
            ("registry", "The Windows registry isn't part of KScript 2.", null));

        private static IReadOnlyDictionary<string, RetiredInfo> Table(params (string Name, string Replacement, string Rename)[] rows) =>
            rows.ToDictionary(r => r.Name, r => new RetiredInfo(r.Name, r.Replacement, r.Rename), StringComparer.Ordinal);
    }
}
