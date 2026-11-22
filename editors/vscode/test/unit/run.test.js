'use strict';

const assert = require('assert');
const path = require('path');
const { test } = require('node:test');
const { stripAnsi, parseRunError, untitledPath, isTestFile, parseJUnit, filterFor, namePattern, outcomeOf, loadError } = require('../../src/run');

test('finds the error a script stopped with, colours or not', () => {
  const plain = "hi\n   ERROR  /work/my app/hello.kscript:3:5: Type: '-' needs a number, but got the string 'a'.\n";
  assert.deepStrictEqual(parseRunError(plain), { file: '/work/my app/hello.kscript', line: 3, column: 5, message: "Type: '-' needs a number, but got the string 'a'." });
  const coloured = '\u001b[1;97;41m ERROR \u001b[0m C:\\work\\hello.kscript:12: Name: $x is not defined.\r\n';
  assert.deepStrictEqual(parseRunError(coloured), { file: 'C:\\work\\hello.kscript', line: 12, column: 1, message: 'Name: $x is not defined.' });
  assert.strictEqual(parseRunError('all fine\n'), null);
  assert.strictEqual(stripAnsi('\u001b[32mok\u001b[0m'), 'ok');
});

test('gives a new unsaved script a path to run from', () => {
  assert.strictEqual(untitledPath('Untitled-1', 'kscript-code', '/tmp'), path.join('/tmp', 'kscript-untitled', 'Untitled-1.kscript'));
  assert.strictEqual(untitledPath('Untitled-2', 'kscript', '/tmp'), path.join('/tmp', 'kscript-untitled', 'Untitled-2.ks.xml'));
  assert.strictEqual(untitledPath('my script.kscript', 'kscript-code', '/tmp'), path.join('/tmp', 'kscript-untitled', 'my-script.kscript'));
  assert.strictEqual(untitledPath('', 'kscript-code', '/tmp'), path.join('/tmp', 'kscript-untitled', 'Untitled.kscript'));
});

test('knows a test file by its name', () => {
  for (const name of ['a.test.kscript', '/x/cart.test.ks.xml', 'C:\\x\\cart.TEST.ks.xml']) assert.ok(isTestFile(name), name);
  for (const name of ['a.kscript', 'test.kscript', 'a.test.js', 'latest.ks.xml', 'cart.test.xml']) assert.ok(!isTestFile(name), name);
});

const report = `<?xml version="1.0" encoding="utf-8"?>
<testsuites name="kscript test" tests="5" failures="1" errors="1" skipped="1" time="0.1">
  <testsuite name="tests/strings.test.kscript" file="tests/strings.test.kscript" tests="5">
    <testcase name="slugify › lower-cases words" classname="tests/strings.test.kscript" file="tests/strings.test.kscript" line="2" time="0.001" />
    <testcase name="slugify › slug of 'One'" classname="tests/strings.test.kscript" file="tests/strings.test.kscript" line="5" time="0.002" />
    <testcase name="slugify › slug of 'Two Words'" classname="tests/strings.test.kscript" file="tests/strings.test.kscript" line="5" time="0.003">
      <failure message="Expected 'two-words', but got 'two words'." type="Assertion">Expected 'two-words', but got 'two words'.
at strings.test.kscript:6:7</failure>
      <system-out>slug &lt;two&gt; &amp; more</system-out>
    </testcase>
    <testcase name="waits" classname="tests/strings.test.kscript" time="0"><skipped message="not yet" /></testcase>
    <testcase name="breaks &quot;badly&quot;" classname="tests/strings.test.kscript" time="0">
      <error message="$x is not defined." type="Name">$x is not defined.</error>
    </testcase>
  </testsuite>
</testsuites>`;

test('reads the tests of a JUnit report', () => {
  const results = parseJUnit(report);
  assert.deepStrictEqual(results.map((r) => [r.name, r.outcome]), [
    ['slugify › lower-cases words', 'passed'],
    ["slugify › slug of 'One'", 'passed'],
    ["slugify › slug of 'Two Words'", 'failed'],
    ['waits', 'skipped'],
    ['breaks "badly"', 'errored'],
  ]);
  assert.strictEqual(results[0].file, 'tests/strings.test.kscript');
  assert.strictEqual(results[2].message, "Expected 'two-words', but got 'two words'.");
  assert.deepStrictEqual(results[2].location, { file: 'strings.test.kscript', line: 6, column: 7 });
  assert.strictEqual(results[2].output, 'slug <two> & more');
  assert.strictEqual(results[4].message, '$x is not defined.');
  assert.strictEqual(results[4].location, undefined);
  assert.deepStrictEqual(parseJUnit(''), []);
});

test('matches a test to its runs', () => {
  const results = parseJUnit(report);
  const plain = { fullName: 'slugify › lower-cases words', template: false };
  const each = { fullName: "slugify › slug of '{$case.text}'", template: true };
  assert.strictEqual(filterFor(plain), 'slugify › lower-cases words');
  assert.strictEqual(filterFor(each), "slugify › slug of '");
  assert.ok(namePattern(each).test("slugify › slug of 'Two Words'"));
  assert.ok(!namePattern(each).test('slugify › lower-cases words'));
  assert.ok(!namePattern({ fullName: 'a.b (c)', template: false }).test('aXb (c)'));

  assert.deepStrictEqual(outcomeOf(plain, results), { outcome: 'passed', seconds: 0.001 });
  const failed = outcomeOf(each, results);
  assert.strictEqual(failed.outcome, 'failed');
  assert.strictEqual(failed.message, "slugify › slug of 'Two Words'\nExpected 'two-words', but got 'two words'.");
  assert.strictEqual(failed.location.line, 6);
  assert.strictEqual(outcomeOf({ fullName: 'waits', template: false }, results).outcome, 'skipped');
  assert.strictEqual(outcomeOf({ fullName: 'breaks "badly"', template: false }, results).outcome, 'errored');
  assert.strictEqual(outcomeOf({ fullName: 'not run', template: false }, results), null);
});

test('reports a file that could not load', () => {
  const xml = '<testsuite><testcase name="(load)" classname="a.test.kscript" time="0"><error message="a.test.kscript:1:1: Syntax: oops">a.test.kscript:1:1: Syntax: oops</error></testcase></testsuite>';
  assert.strictEqual(loadError(parseJUnit(xml)), 'a.test.kscript:1:1: Syntax: oops');
  assert.strictEqual(loadError(parseJUnit(report)), null);
});

test('Open docs searches for the name a hover is about, and nothing else', () => {
  const { docsArgs } = require('../../src/run');
  assert.deepStrictEqual(docsArgs('upper'), ['docs', 'upper']);
  assert.deepStrictEqual(docsArgs(' ->pad_left '), ['docs', '->pad_left']);
  assert.deepStrictEqual(docsArgs(), ['docs']);
  assert.deepStrictEqual(docsArgs({ fsPath: '/x' }), ['docs']);
  // Nothing that could be read as an option or a second argument.
  assert.deepStrictEqual(docsArgs('--port'), ['docs']);
  assert.deepStrictEqual(docsArgs('a b'), ['docs']);
});
