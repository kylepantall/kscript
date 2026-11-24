'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const { manifestContext, manifestSuggestions } = require('../../src/manifest');

function at(source) {
  const offset = source.indexOf('‸');
  return manifestContext(source.replace('‸', ''), offset);
}

const metadata = {
  requiredPackages: { 'KScript.Net': ['client'], 'KScript.Math': ['numbers'] },
  types: { password: ['min', 'max', 'algorithm'], uuid: ['v'] },
};

test('suggests installed library requirements as package values without repeating existing packages', () => {
  const items = manifestSuggestions(at('{ // local packages\n "packages": ["KScript.Math", ‸] }'), metadata);
  assert.deepEqual(items.map(item => item.label), ['KScript.Net']);
  assert.equal(items[0].detail, 'Required by client');
  assert.equal(items[0].snippet, '"KScript.Net"');
});

test('suggests native types and their keys at the matching levels of types', () => {
  const type = manifestSuggestions(at('{"types": {‸}}'), metadata).find(item => item.label === 'uuid');
  assert.equal(type.snippet, '"uuid": { $0 }');
  const context = at('{"types": {"password": {"min":10, "alg‸": "bcrypt"}}}');
  const keys = manifestSuggestions(context, metadata);
  assert.deepEqual(keys.map(item => item.label), ['max', 'algorithm']);
  assert.equal(keys.find(item => item.label === 'algorithm').snippet, '"algorithm"');
  assert.equal(at('{"paths": {"types": "‸"}}'), null);
});
