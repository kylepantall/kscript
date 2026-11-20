'use strict';

const test = require('node:test');
const assert = require('node:assert');
const { endEmptyDocComment, commentEnter } = require('../../src/comments');

test('Enter on an empty /// line ends the comment and keeps the indentation', () => {
  assert.strictEqual(endEmptyDocComment('///', ''), '');
  assert.strictEqual(endEmptyDocComment('  /// ', ''), '  ');
  assert.strictEqual(endEmptyDocComment('\t///', '  '), '\t');
});

test('Enter anywhere else is an ordinary Enter', () => {
  assert.strictEqual(endEmptyDocComment('/// The total.', ''), null);
  assert.strictEqual(endEmptyDocComment('///', 'text after'), null);
  assert.strictEqual(endEmptyDocComment('//', ''), null);
  assert.strictEqual(endEmptyDocComment('x = 1', ''), null);
  assert.strictEqual(endEmptyDocComment('', '///'), null);
});

function fakeVscode() {
  const calls = { typed: [], replaced: [] };
  const vscode = { commands: { executeCommand: async (name, args) => { calls.typed.push([name, args.text]); } } };
  const editor = (text, character, isEmpty = true) => ({
    selection: { isEmpty, active: { line: 0, character } },
    document: { lineAt: () => ({ text, range: 'line 0' }) },
    edit: async (fn) => fn({ replace: (range, value) => calls.replaced.push([range, value]) }),
  });
  return { calls, run: (e) => commentEnter(vscode, e), editor };
}

test('the command clears an empty /// line, and types a newline otherwise', async () => {
  const { calls, run, editor } = fakeVscode();
  await run(editor('  /// ', 6));
  await run(editor('  /// Sums.', 11));
  await run(editor('  ///', 5, false));
  assert.deepStrictEqual(calls.replaced, [['line 0', '  ']]);
  assert.deepStrictEqual(calls.typed, [['default:type', '\n'], ['default:type', '\n']]);
});
