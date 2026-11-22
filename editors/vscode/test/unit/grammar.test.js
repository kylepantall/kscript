'use strict';

// Tokenizes KScript with the extension's TextMate grammar (the same engine VS Code uses) and checks the scopes that
// themes colour: control-flow and declaration tags, expressions, and JSON bodies.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const textmate = require('vscode-textmate');
const oniguruma = require('vscode-oniguruma');

const grammarPath = path.resolve(__dirname, '../../syntaxes/kscript.tmLanguage.json');

// A tiny stand-in for VS Code's JSON grammar, so embedded JSON can be checked without downloading it.
const jsonGrammar = {
  scopeName: 'source.json',
  patterns: [
    { name: 'support.type.property-name.json', match: '"[^"]*"(?=\\s*:)' },
    { name: 'string.quoted.double.json', match: '"[^"]*"' },
    { name: 'constant.numeric.json', match: '-?\\d+(\\.\\d+)?' },
    { name: 'constant.language.json', match: '\\b(true|false|null)\\b' },
  ],
};

async function load() {
  const wasm = fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm')).buffer;
  await oniguruma.loadWASM(wasm);
  const registry = new textmate.Registry({
    onigLib: Promise.resolve({ createOnigScanner: (p) => new oniguruma.OnigScanner(p), createOnigString: (s) => new oniguruma.OnigString(s) }),
    loadGrammar: async (scope) => {
      if (scope === 'source.kscript') return textmate.parseRawGrammar(fs.readFileSync(grammarPath, 'utf8'), grammarPath);
      if (scope === 'source.json') return textmate.parseRawGrammar(JSON.stringify(jsonGrammar), 'json.json');
      return null;
    },
  });
  return registry.loadGrammar('source.kscript');
}

/** Every token as { text, scopes }. */
function tokenize(grammar, source) {
  const out = [];
  let stack = textmate.INITIAL;
  for (const line of source.split('\n')) {
    const r = grammar.tokenizeLine(line, stack);
    for (const t of r.tokens) out.push({ text: line.substring(t.startIndex, t.endIndex), scopes: t.scopes.join(' ') });
    stack = r.ruleStack;
  }
  return out;
}

const scopeOf = (tokens, text) => (tokens.find((t) => t.text === text) || { scopes: '' }).scopes;

test('control flow and declaration tags are coloured like keywords', async () => {
  const grammar = await load();
  const tokens = tokenize(grammar, '<kscript>\n  <let name="n" value="3" />\n  <if test="$n gt 2"><echo>big</echo></if>\n  <fn name="f" value="1" />\n</kscript>');
  assert.match(scopeOf(tokens, 'if'), /keyword\.control\.flow\.kscript/);
  assert.match(scopeOf(tokens, 'let'), /storage\.type\.kscript/);
  assert.match(scopeOf(tokens, 'fn'), /storage\.type\.kscript/);
  assert.match(scopeOf(tokens, 'echo'), /^source\.kscript meta\.tag\.kscript entity\.name\.tag\.kscript$/, 'other tags stay tags');
  assert.match(scopeOf(tokens, '$n'), /variable\.other\.kscript/);
});

test('tags that only start with a keyword are ordinary tags', async () => {
  const grammar = await load();
  const tokens = tokenize(grammar, '<kscript><format-date /><letter /></kscript>');
  assert.doesNotMatch(scopeOf(tokens, 'letter'), /storage\.type/);
});

test('the body of <echo format="json"> is JSON', async () => {
  const grammar = await load();
  const tokens = tokenize(grammar, '<kscript>\n  <echo format="json">\n    { "name": "Joan", "age": 82, "ok": true }\n  </echo>\n  <echo>{ $x }</echo>\n</kscript>');
  assert.match(scopeOf(tokens, '"name"'), /meta\.embedded\.block\.json .*support\.type\.property-name\.json/);
  assert.match(scopeOf(tokens, '82'), /constant\.numeric\.json/);
  assert.match(scopeOf(tokens, 'true'), /constant\.language\.json/);
  assert.match(scopeOf(tokens, 'format'), /entity\.other\.attribute-name\.kscript/, 'the start tag keeps its attribute colours');
  assert.match(scopeOf(tokens, '$x'), /variable\.other\.kscript/, 'the grammar is back to KScript after </echo>');
  assert.doesNotMatch(scopeOf(tokens, '$x'), /json/);
});

test('shorthand conditions are coloured as expressions inside the start tag', async () => {
  const grammar = await load();
  const tokens = tokenize(grammar, '<kscript>\n  <if $user.is_logged_in and $n > 3>\n    <echo>hi</echo>\n  </if>\n  <while $list->count() gt 0><break /></while>\n  <match $total></match>\n</kscript>');
  assert.match(scopeOf(tokens, 'if'), /keyword\.control\.flow\.kscript/);
  assert.match(scopeOf(tokens, '$user'), /meta\.embedded\.expression\.kscript .*variable\.other\.kscript/);
  assert.match(scopeOf(tokens, 'and'), /keyword\.operator\.word\.kscript/);
  assert.match(scopeOf(tokens, '3'), /constant\.numeric\.kscript/, 'a spaced > is an operator, not the end of the tag');
  assert.match(scopeOf(tokens, 'count'), /entity\.name\.function\.method\.kscript/, '-> does not end the tag');
  assert.match(scopeOf(tokens, '$total'), /variable\.other\.kscript/);
  assert.match(scopeOf(tokens, 'echo'), /^source\.kscript meta\.tag\.kscript entity\.name\.tag\.kscript$/, 'the tag closes before its body');
  assert.match(scopeOf(tokens, 'break'), /keyword\.control\.flow\.kscript/);
});

test('if with a test attribute keeps its attribute colours', async () => {
  const grammar = await load();
  const tokens = tokenize(grammar, '<kscript><if test="$ok"><echo>x</echo></if></kscript>');
  assert.match(scopeOf(tokens, 'test'), /entity\.other\.attribute-name\.kscript/);
  assert.doesNotMatch(scopeOf(tokens, 'test'), /meta\.embedded\.expression/);
});

test('doc comments colour their tags and the parameters they name', async () => {
  const grammar = await load();
  const tokens = tokenize(grammar, '<kscript>\n  <!--- Doubles a number, like @twice.\n    @param x The number.\n    @arg $times How many.\n    @returns Twice x.\n    @llm Use for numbers.\n  -->\n  <fn name="double" params="x, times" value="$x * $times" />\n  <!-- @param plain -->\n</kscript>');
  assert.match(scopeOf(tokens, '@param'), /comment\.block\.documentation\.kscript storage\.type\.class\.doc\.kscript/);
  assert.match(scopeOf(tokens, 'x'), /comment\.block\.documentation\.kscript variable\.parameter\.kscript/);
  assert.match(scopeOf(tokens, '@arg'), /storage\.type\.class\.doc\.kscript/);
  assert.match(scopeOf(tokens, '$times'), /variable\.parameter\.kscript/);
  assert.match(scopeOf(tokens, '@returns'), /storage\.type\.class\.doc\.kscript/);
  assert.match(scopeOf(tokens, '@llm'), /storage\.type\.class\.doc\.kscript/);
  assert.doesNotMatch(tokens.filter((t) => t.text.includes('@twice')).map((t) => t.scopes).join(), /storage\.type/, 'a tag only counts at the start of a line');
  const plain = tokens.filter((t) => t.text.includes('@param plain'));
  assert.ok(plain.length === 1 && /comment\.block\.kscript/.test(plain[0].scopes), 'a plain comment has no tags');
});
