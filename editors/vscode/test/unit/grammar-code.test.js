'use strict';

// Tokenizes .kscript (the brace-and-newline format) with its TextMate grammar and checks the scopes themes colour.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const textmate = require('vscode-textmate');
const oniguruma = require('vscode-oniguruma');

const grammarPath = path.resolve(__dirname, '../../syntaxes/kscript-code.tmLanguage.json');

let loaded;
function load() {
  loaded ??= (async () => {
    const wasm = fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm')).buffer;
    await oniguruma.loadWASM(wasm);
    const registry = new textmate.Registry({
      onigLib: Promise.resolve({ createOnigScanner: (p) => new oniguruma.OnigScanner(p), createOnigString: (s) => new oniguruma.OnigString(s) }),
      // Languages embedded in ``` blocks (source.sql, text.tex.latex, ...) come from VS Code itself: stand-ins here.
      loadGrammar: async (scope) => (scope === 'source.kscript-code'
        ? textmate.parseRawGrammar(fs.readFileSync(grammarPath, 'utf8'), grammarPath)
        : textmate.parseRawGrammar(JSON.stringify({ scopeName: scope, patterns: [] }), scope + '.json')),
    });
    return registry.loadGrammar('source.kscript-code');
  })();
  return loaded;
}

/** Every token as { text, scopes }. */
function tokenize(grammar, source) {
  const out = [];
  let stack = textmate.INITIAL;
  for (const line of source.split('\n')) {
    const r = grammar.tokenizeLine(line, stack);
    for (const t of r.tokens) out.push({ text: line.substring(t.startIndex, t.endIndex).trim(), scopes: t.scopes.join(' ') });
    stack = r.ruleStack;
  }
  return out;
}

const scopeOf = (tokens, text, nth = 0) => (tokens.filter((t) => t.text === text)[nth] || { scopes: '' }).scopes;

test('match arms: values, _ and when', async () => {
  const tokens = tokenize(await load(), [
    'match $n {',
    '  1, 2 => echo "low"',
    '  _ when $n > 9 and $ok => { echo "high" }',
    '  _ => echo "other"',
    '}',
    'if $n lt 3 { }',
  ].join('\n'));
  assert.match(scopeOf(tokens, 'match'), /keyword\.control\.flow\.kscript/);
  assert.match(scopeOf(tokens, '_'), /keyword\.control\.flow\.kscript/);
  assert.match(scopeOf(tokens, '_', 1), /keyword\.control\.flow\.kscript/);
  assert.match(scopeOf(tokens, 'when'), /keyword\.control\.flow\.kscript/);
  assert.match(scopeOf(tokens, '=>'), /storage\.type\.function\.arrow\.kscript/);
  assert.match(scopeOf(tokens, 'and'), /keyword\.operator\.word\.kscript/);
  // Comparisons are symbols in a .kscript file: the XML words aren't operators here.
  assert.doesNotMatch(tokens.filter((t) => t.text.includes('lt')).map((t) => t.scopes).join(' '), /keyword\.operator\.word/);
});

test('keywords, functions and calls', async () => {
  const tokens = tokenize(await load(), [
    'import KScript.IO',
    'fn double(x: int) => $x * 2',
    'for item at i in $list {',
    '  if $item > 2 { break } else { @double($item) }',
    '}',
    'throw Validation "Bad"',
  ].join('\n'));
  assert.match(scopeOf(tokens, 'import'), /keyword\.control\.import\.kscript/);
  assert.match(scopeOf(tokens, 'fn'), /storage\.type\.function\.kscript/);
  assert.match(scopeOf(tokens, 'double'), /entity\.name\.function\.kscript/);
  assert.match(scopeOf(tokens, '=>'), /storage\.type\.function\.arrow\.kscript/);
  for (const kw of ['for', 'at', 'in', 'if', 'break', 'else', 'throw']) {
    assert.match(scopeOf(tokens, kw), /keyword\.control\.flow\.kscript/, kw);
  }
  assert.match(scopeOf(tokens, '@double'), /entity\.name\.function\.kscript/);
  assert.match(scopeOf(tokens, '$item'), /variable\.other\.kscript/);
  assert.match(scopeOf(tokens, 'Validation'), /entity\.name\.type\.kscript/);
});

test('assignment targets and constants', async () => {
  const tokens = tokenize(await load(), "total = 0\ncount: int = $raw\ntotal += 1\nconst tax = 0.2\nok = $a == true");
  assert.match(scopeOf(tokens, 'total'), /variable\.other\.assignment\.kscript/);
  assert.match(scopeOf(tokens, 'total', 1), /variable\.other\.assignment\.kscript/);
  assert.match(scopeOf(tokens, 'count'), /variable\.other\.assignment\.kscript/);
  assert.match(scopeOf(tokens, 'tax'), /variable\.other\.constant\.kscript/);
  assert.match(scopeOf(tokens, '0.2'), /constant\.numeric\.kscript/);
  assert.match(scopeOf(tokens, 'true'), /constant\.language\.kscript/);
  assert.match(scopeOf(tokens, '=='), /keyword\.operator\.kscript/);
});

test('double quotes fill in $name and { }; single quotes are kept as written', async () => {
  const tokens = tokenize(await load(), "echo \"Hi $name, { @upper('x') } \\\" done\"\nx = $s->replace('a', 'b')");
  assert.match(scopeOf(tokens, 'echo'), /support\.function\.builtin\.kscript/);
  assert.match(scopeOf(tokens, '$name'), /string\.quoted\.double\.kscript variable\.other\.kscript/);
  assert.match(scopeOf(tokens, '@upper'), /meta\.interpolation\.kscript .*entity\.name\.function\.kscript/);
  assert.match(scopeOf(tokens, 'x'), /string\.quoted\.single\.kscript/);
  assert.match(scopeOf(tokens, '\\"'), /constant\.character\.escape\.kscript/);
  assert.match(scopeOf(tokens, 'replace'), /support\.function\.kscript/);
  assert.match(scopeOf(tokens, 'a'), /string\.quoted\.single\.kscript/);
});

test('elements and their attributes', async () => {
  const tokens = tokenize(await load(), 'Row(gap: 8, wrap) {\n  Button(variant: primary, on_click: "go()") "Play"\n  div {\n  }\n}');
  assert.match(scopeOf(tokens, 'Row'), /entity\.name\.tag\.kscript/);
  assert.match(scopeOf(tokens, 'gap'), /entity\.other\.attribute-name\.kscript/);
  assert.match(scopeOf(tokens, 'Button'), /entity\.name\.tag\.kscript/);
  assert.match(scopeOf(tokens, 'on_click'), /entity\.other\.attribute-name\.kscript/);
  assert.match(scopeOf(tokens, 'div'), /entity\.name\.tag\.kscript/);
  assert.match(scopeOf(tokens, 'Play'), /string\.quoted\.double\.kscript/);
});

test('comments and doc comments', async () => {
  const tokens = tokenize(await load(), '/// Greets someone.\n/// @param who The person.\n/// @returns Text.\n// plain\n/* block */\nfn greet(who) => $who');
  assert.match(scopeOf(tokens, '@param'), /comment\.line\.documentation\.kscript storage\.type\.class\.doc\.kscript/);
  assert.match(scopeOf(tokens, 'who'), /comment\.line\.documentation\.kscript variable\.parameter\.kscript/);
  assert.match(scopeOf(tokens, '@returns'), /storage\.type\.class\.doc\.kscript/);
  assert.match(scopeOf(tokens, 'plain'), /comment\.line\.double-slash\.kscript/);
  assert.match(scopeOf(tokens, 'block'), /comment\.block\.kscript/);

  const tags = tokenize(await load(), '/// Calls @greet for you.\n/// @arg $who The person.\n/// @argument times How often.\n/// @example\n///   @greet(\'a\')\n/// @pitfall Not for groups.\n/// @agent hide\nfn f(who, times) => $who');
  assert.match(scopeOf(tags, '@arg'), /storage\.type\.class\.doc\.kscript/);
  assert.match(scopeOf(tags, '$who'), /comment\.line\.documentation\.kscript variable\.parameter\.kscript/);
  assert.match(scopeOf(tags, '@argument'), /storage\.type\.class\.doc\.kscript/);
  assert.match(scopeOf(tags, 'times'), /variable\.parameter\.kscript/);
  assert.match(scopeOf(tags, '@pitfall'), /storage\.type\.class\.doc\.kscript/);
  assert.match(scopeOf(tags, '@agent'), /storage\.type\.class\.doc\.kscript/);
  assert.doesNotMatch(tags.filter((t) => t.text.includes('@greet')).map((t) => t.scopes).join(), /storage\.type/, 'a tag only counts at the start of a line');

  const more = tokenize(await load(), 'x = 1 /* inline */\ny = [\n  1, /* first */\n  2 // second\n]\n/*\n  multi\n  line\n*/\necho "after"');
  assert.match(scopeOf(more, 'inline'), /comment\.block\.kscript/);
  assert.match(scopeOf(more, 'first'), /comment\.block\.kscript/);
  assert.match(scopeOf(more, 'second'), /comment\.line\.double-slash\.kscript/);
  assert.match(scopeOf(more, 'line'), /comment\.block\.kscript/);
  assert.match(scopeOf(more, 'after'), /string\.quoted\.double\.kscript/);
});

test('types: with lists, annotations, rules and typed literals', async () => {
  const tokens = tokenize(await load(), [
    'type Username with [handle, Auditable] {',
    '  max: 16',
    '  pattern: /^@[a-z_]+$/i',
    '  validate: not $value->name()->starts_with(\'_\'), "No leading _"',
    '  level: int',
    '}',
    'id: uuid(v: 4) = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f',
    'e: email = joan@example.com',
    'half = 10 / 2 / 5',
  ].join('\n'));
  assert.match(scopeOf(tokens, 'type'), /storage\.type\.kscript/);
  assert.match(scopeOf(tokens, 'Username'), /entity\.name\.type\.kscript/);
  assert.match(scopeOf(tokens, 'with'), /storage\.modifier\.kscript/);
  assert.match(scopeOf(tokens, 'handle'), /support\.type\.builtin\.kscript/);
  assert.match(scopeOf(tokens, 'Auditable'), /entity\.name\.type\.kscript/);
  assert.match(scopeOf(tokens, 'max'), /keyword\.other\.rule\.kscript/);
  assert.match(scopeOf(tokens, 'validate'), /keyword\.other\.rule\.kscript/);
  assert.match(scopeOf(tokens, '/^@[a-z_]+$/i'), /string\.regexp\.kscript/);
  assert.match(scopeOf(tokens, 'int'), /support\.type\.builtin\.kscript/);
  assert.doesNotMatch(scopeOf(tokens, 'level'), /keyword\.other\.rule/);
  assert.match(scopeOf(tokens, 'id'), /variable\.other\.assignment\.kscript/);
  assert.match(scopeOf(tokens, 'uuid'), /support\.type\.builtin\.kscript/);
  assert.match(scopeOf(tokens, '3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f'), /constant\.other\.uuid\.kscript/);
  assert.match(scopeOf(tokens, 'email'), /support\.type\.builtin\.kscript/);
  assert.match(scopeOf(tokens, 'joan@example.com'), /string\.unquoted\.email\.kscript/);
  assert.ok(!tokens.some((t) => /string\.regexp/.test(t.scopes) && t.text.includes('2')), 'division is not a regex');
});

test('every sample tokenizes without leaving a string or block open', async () => {
  const grammar = await load();
  const root = path.resolve(__dirname, '../../../../samples');
  const files = [...fs.readdirSync(path.join(root, 'code-format'), { recursive: true })]
    .filter((f) => f.endsWith('.kscript')).map((f) => path.join(root, 'code-format', f));
  files.push(path.join(root, 'life-lab', 'desktop.kscript'));
  assert.ok(files.length > 3);
  for (const file of files) {
    let stack = textmate.INITIAL;
    for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) stack = grammar.tokenizeLine(line, stack).ruleStack;
    assert.strictEqual(stack.depth, 1, `${path.basename(file)} ends inside a rule`);
  }
});

test('a backslash at the end of a line joins a string to the next line', async () => {
  const tokens = tokenize(await load(), "echo \"Hello, \\\n    $name\"\nx = 'abc \\\n  def'\ny = 1");
  assert.match(scopeOf(tokens, '\\'), /string\.quoted\.double\.kscript constant\.character\.escape\.line-continuation\.kscript/);
  assert.match(scopeOf(tokens, '$name'), /string\.quoted\.double\.kscript variable\.other\.kscript/);
  assert.match(scopeOf(tokens, '\\', 1), /string\.quoted\.single\.kscript constant\.character\.escape\.line-continuation\.kscript/);
  assert.match(scopeOf(tokens, 'def'), /string\.quoted\.single\.kscript/);
  assert.match(scopeOf(tokens, 'y'), /variable\.other\.assignment\.kscript/);
});

test('``` blocks: text, a language or a <Type>, closed by a fence of their own', async () => {
  const tokens = tokenize(await load(), [
    'echo ```',
    '  Hi $name',
    '  ```',
    'query = ```sql',
    '  SELECT 1',
    '  ```',
    'repo = ```<Repo>',
    '  { "a": 1 }',
    '  ```',
    'Math(id: x) ````latex',
    '  ```',
    '  \\frac{1}{2}',
    '  ````',
    'z = 2',
  ].join('\n'));
  assert.match(scopeOf(tokens, '$name'), /string\.unquoted\.block\.kscript variable\.other\.kscript/);
  assert.match(scopeOf(tokens, 'sql'), /meta\.block\.fenced\.kscript entity\.name\.tag\.language\.kscript/);
  assert.match(scopeOf(tokens, 'SELECT 1'), /meta\.embedded\.block\.sql/);
  assert.match(scopeOf(tokens, '<Repo>'), /entity\.name\.type\.kscript/);
  assert.match(scopeOf(tokens, '\\frac{1}{2}'), /meta\.embedded\.block\.latex/);
  // Three backticks don't close a block opened with four.
  assert.match(scopeOf(tokens, '```', 6), /meta\.embedded\.block\.latex/);
  assert.match(scopeOf(tokens, 'z'), /variable\.other\.assignment\.kscript/);
});
