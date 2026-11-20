'use strict';

const test = require('node:test');
const assert = require('node:assert');
const { closingTagFor, autoCloseChange } = require('../../src/autoclose');

test('a finished start tag gets its end tag', () => {
  assert.strictEqual(closingTagFor('<kscript>\n  <echo>', '\n</kscript>'), 'echo');
  assert.strictEqual(closingTagFor('<if test="$a > 1">', ''), 'if');
  assert.strictEqual(closingTagFor("<for each='p' in=\"$people\">", ''), 'for');
  assert.strictEqual(closingTagFor('<KScript.Routes>', ''), 'KScript.Routes');
  assert.strictEqual(closingTagFor('<fn name="f"\n    params="a">', ''), 'fn');
});

test('anything else is left alone', () => {
  assert.strictEqual(closingTagFor('<let name="x" value="1" />', ''), null);
  assert.strictEqual(closingTagFor('<if test="$a >', ''), null);
  assert.strictEqual(closingTagFor('<echo>{ $x->', ''), null);
  assert.strictEqual(closingTagFor('</echo>', ''), null);
  assert.strictEqual(closingTagFor('<!-- a -->', ''), null);
  assert.strictEqual(closingTagFor('x >', ''), null);
  assert.strictEqual(closingTagFor('<echo>', '</echo>'), null);
});

test('only a typed > triggers it', () => {
  const text = '<kscript><echo></kscript>';
  const document = { offsetAt: (p) => p, getText: () => text };
  assert.deepStrictEqual(autoCloseChange(document, { text: '>', rangeLength: 0, range: { start: 14 } }), { offset: 15, name: 'echo' });
  assert.strictEqual(autoCloseChange(document, { text: '>', rangeLength: 1, range: { start: 14 } }), null);
  assert.strictEqual(autoCloseChange(document, { text: 'a', rangeLength: 0, range: { start: 14 } }), null);
  assert.strictEqual(autoCloseChange({ offsetAt: (p) => p, getText: () => 'a > b' }, { text: '>', rangeLength: 0, range: { start: 2 } }), null);
});
