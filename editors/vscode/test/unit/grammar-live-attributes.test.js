'use strict';

// Tokenizes @-commands on HTML tags (@on-click(call: $add, with: [...]), @each(Note->all(), as: n)) with the live
// attributes injection, over a small stand-in for VS Code's HTML grammar whose tag, like the real one, ends at '>'.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const textmate = require('vscode-textmate');
const oniguruma = require('vscode-oniguruma');

const syntaxes = path.resolve(__dirname, '../../syntaxes');

// Like VS Code's text.html.basic: a start tag runs from <name to the first '>', and holds name="value" attributes.
const html = {
  scopeName: 'text.html.basic',
  patterns: [{
    begin: '(<)([a-z]+)',
    beginCaptures: { 2: { name: 'entity.name.tag.html' } },
    end: '/?>',
    name: 'meta.tag.structure.start.html',
    patterns: [{
      match: '([a-z][\\w-]*)(=)("[^"]*")',
      captures: { 1: { name: 'entity.other.attribute-name.html' }, 3: { name: 'string.quoted.double.html' } },
    }],
  }],
};

let loaded;
function load(scope) {
  loaded ??= (async () => {
    const wasm = fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm')).buffer;
    await oniguruma.loadWASM(wasm);
    const files = {
      'source.kscript-code': 'kscript-code.tmLanguage.json',
      'kscript.live-attributes.injection': 'kscript-live-attributes.injection.json',
    };
    return new textmate.Registry({
      onigLib: Promise.resolve({ createOnigScanner: (p) => new oniguruma.OnigScanner(p), createOnigString: (s) => new oniguruma.OnigString(s) }),
      getInjections: (name) => (name === 'text.html.basic' || name === 'source.kscript-code' ? ['kscript.live-attributes.injection'] : []),
      loadGrammar: async (name) => {
        if (name === 'text.html.basic') return textmate.parseRawGrammar(JSON.stringify(html), 'html.json');
        if (files[name]) return textmate.parseRawGrammar(fs.readFileSync(path.join(syntaxes, files[name]), 'utf8'), files[name]);
        return textmate.parseRawGrammar(JSON.stringify({ scopeName: name, patterns: [] }), name + '.json');
      },
    });
  })();
  return loaded.then((registry) => registry.loadGrammar(scope));
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

const scopesOf = (tokens, text) => tokens.filter((t) => t.text === text).map((t) => t.scopes);

const button = [
  '<button',
  '  type="button"',
  '  @each(NoteColor->keys(), as: c)',
  '  class="pad"',
  '  title="New note"',
  '  @if($count > 3)',
  '  aria-label="New note"',
  '  @drag(copy: true, value: $c, ghost: "note-ghost")',
  "  @on-dblclick(call: $add, with: ['yellow', $event.x - 124])",
  '  @on-drop(call: $add, with: [$event.drag_value])>x</button>',
].join('\n');

test('argument names inside @-commands on a tag are coloured, and their values are expressions', async () => {
  const tokens = tokenize(await load('text.html.basic'), button);
  for (const name of ['as', 'copy', 'value', 'ghost', 'call', 'with']) {
    for (const scopes of scopesOf(tokens, name)) assert.match(scopes, /entity\.other\.attribute-name\.kscript/, name);
  }
  assert.match(scopesOf(tokens, '$add')[0], /meta\.embedded\.expression\.kscript .*variable\.other\.kscript/);
  assert.match(scopesOf(tokens, 'drag_value')[0], /variable\.other\.property\.kscript/);
  assert.match(scopesOf(tokens, 'keys')[0], /support\.function\.kscript/);
  assert.match(scopesOf(tokens, '@each')[0], /keyword\.control\.directive\.kscript/);
});

test('-> and > inside an @-command do not end the tag, so later attributes stay HTML attributes', async () => {
  const tokens = tokenize(await load('text.html.basic'), button);
  for (const attribute of ['class', 'title', 'aria-label']) {
    assert.match(scopesOf(tokens, attribute)[0] ?? '', /meta\.tag.*entity\.other\.attribute-name\.html/, attribute);
  }
  assert.match(scopesOf(tokens, '>')[0] ?? '', /keyword\.operator/, 'the > in @if($count > 3) is an operator');
  assert.doesNotMatch(scopesOf(tokens, 'x</button>')[0] ?? '', /meta\.tag/, 'the tag ends at its own >');
});

test('named arguments are coloured in .kscript calls, without taking typed parameters or ternaries', async () => {
  const tokens = tokenize(await load('source.kscript-code'), [
    'fn add(color: string, size: Note) {',
    '  let r = @range(1, 10, step: 2)',
    "  let m = { name: 'x', size: 3 }",
    '  let t = $ok ? a : b',
    "  @emit_event('note-deleted', data: $id)",
    '}',
  ].join('\n'));
  for (const name of ['step', 'name', 'data']) assert.match(scopesOf(tokens, name)[0], /entity\.other\.attribute-name\.kscript/, name);
  assert.match(scopesOf(tokens, 'string')[0], /support\.type\.builtin\.kscript/, 'a typed parameter keeps its type colour');
  assert.match(scopesOf(tokens, 'Note')[0], /entity\.name\.type\.kscript/);
  assert.doesNotMatch(scopesOf(tokens, 'a')[0] ?? '', /attribute-name/, 'a ternary branch is not an argument name');
});
