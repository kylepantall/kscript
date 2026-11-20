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
      loadGrammar: async (scope) => (scope === 'source.kscript-code' ? textmate.parseRawGrammar(fs.readFileSync(grammarPath, 'utf8'), grammarPath) : null),
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

test('double quotes are text that interpolates; single quotes are expression strings', async () => {
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
  const tokens = tokenize(await load(), 'Row(gap: 8, wrap) {\n  Button(variant: primary, on-click: "go()") "Play"\n  div {\n  }\n}');
  assert.match(scopeOf(tokens, 'Row'), /entity\.name\.tag\.kscript/);
  assert.match(scopeOf(tokens, 'gap'), /entity\.other\.attribute-name\.kscript/);
  assert.match(scopeOf(tokens, 'Button'), /entity\.name\.tag\.kscript/);
  assert.match(scopeOf(tokens, 'on-click'), /entity\.other\.attribute-name\.kscript/);
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
