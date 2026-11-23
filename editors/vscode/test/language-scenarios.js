'use strict';

const assert = require('assert').strict;
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');
const { isTestFile, parseJUnit, filterFor, namePattern } = require('../src/run');
const corpus = path.resolve(__dirname, '../../../samples/scripts/tests/language');

// Find a cursor in the same buffer sent to the server (LSP columns count UTF-16 code units).
function position(text, anchor, offset = 0) {
  const index = text.indexOf(anchor);
  assert.notEqual(index, -1, 'missing cursor anchor ' + anchor);
  const lines = text.slice(0, index + offset).split('\n');
  return { line: lines.length - 1, character: lines.at(-1).length };
}

function diagnosticsAfter(connection, method, params) {
  const { uri, version } = params.textDocument;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      subscription.dispose();
      reject(new Error('Timed out waiting for diagnostics for ' + uri + ' version ' + version));
    }, 10000);
    const subscription = connection.onNotification('textDocument/publishDiagnostics', (published) => {
      if (published.uri === uri && published.version === version) {
        clearTimeout(timer);
        subscription.dispose();
        resolve(published.diagnostics);
      }
    });
    connection.sendNotification('textDocument/' + method, params).catch((err) => {
      clearTimeout(timer);
      subscription.dispose();
      reject(err);
    });
  });
}

async function normalizers(connection) {
  const file = path.join(corpus, 'normalizers.test.kscript');
  const uri = pathToFileURL(file).href;
  const text = fs.readFileSync(file, 'utf8');
  const textDocument = { uri };
  const request = (method, params = {}) => connection.sendRequest(method, { textDocument, ...params });
  const open = await diagnosticsAfter(connection, 'didOpen', {
    textDocument: { uri, languageId: 'kscript-code', version: 1, text },
  });
  assert.deepEqual(open, [], 'the runnable normalizer suite opens cleanly');

  const helper = position(text, '@trim_key($value)', 4);
  const declaration = position(text, 'fn trim_key(', 3);
  const definitions = await request('textDocument/definition', { position: helper });
  assert.deepEqual(definitions.map((d) => [d.uri, d.range.start]), [[uri, declaration]]);
  const completions = await request('textDocument/completion', { position: helper });
  assert.ok(completions.items.some((i) => i.label === 'trim_key' && i.detail.includes('key: string')));
  const rename = await request('textDocument/rename', { position: helper, newName: 'clean_key' });
  const editLines = rename.changes[uri].map((edit) => {
    assert.equal(edit.newText, 'clean_key');
    return edit.range.start.line;
  }).sort((a, b) => a - b);
  assert.deepEqual(editLines, [helper.line, declaration.line, position(text, '$key->trim_key(').line]);

  const originalTests = (await request('kscript/tests')).tests.map((item) => item.fullName);
  const testName = 'a forward-declared helper sees the raw key before validation';
  assert.ok(originalTests.includes(testName), 'expected the normalizer test in ' + JSON.stringify(originalTests));
  const invalid = text.replace('@trim_key($value)', '@missing_key($value)');
  const errors = await diagnosticsAfter(connection, 'didChange', {
    textDocument: { uri, version: 2 }, contentChanges: [{ text: invalid }],
  });
  assert.ok(errors.some((d) => d.code === 'KS5001' && d.range.start.line === helper.line));

  const newTestName = 'normalizing through a function reference';
  const fixed = invalid.replace('@missing_key($value)', '@trim_key').replace(testName, newTestName);
  assert.deepEqual(await diagnosticsAfter(connection, 'didChange', {
    textDocument: { uri, version: 3 }, contentChanges: [{ text: fixed }],
  }), [], 'the diagnostic clears when the edit supplies a reference to the local function');
  assert.deepEqual((await request('kscript/tests')).tests.map((item) => item.fullName),
    originalTests.map((name) => name === testName ? newTestName : name), 'discovery follows the unsaved buffer');
  await connection.sendNotification('textDocument/didClose', { textDocument });
}

async function checkoutEdits(connection) {
  const file = path.join(corpus, 'checkout-workflow.test.kscript');
  const uri = pathToFileURL(file).href;
  const textDocument = { uri };
  const text = fs.readFileSync(file, 'utf8');
  const diagnostics = await diagnosticsAfter(connection, 'didOpen', {
    textDocument: { uri, languageId: 'kscript-code', version: 1, text },
  });
  assert.deepEqual(diagnostics, [], 'checkout opens cleanly with its record methods and catches');
  const definitions = await connection.sendRequest('textDocument/definition', {
    textDocument, position: position(text, '@checkout($lines', 4),
  });
  assert.deepEqual(definitions.map((d) => [d.uri, d.range.start]), [[uri, position(text, 'fn checkout(', 3)]]);

  const invalid = text.replace('unit_price: GBP4.50', 'unit_price: GBP4.501');
  const errors = await diagnosticsAfter(connection, 'didChange', {
    textDocument: { uri, version: 2 }, contentChanges: [{ text: invalid }],
  });
  const precision = errors.filter((d) => d.code === 'KS5012');
  assert.deepEqual(precision.map((d) => d.range), [{
    start: position(invalid, 'GBP4.501'), end: position(invalid, 'GBP4.501', 8),
  }], 'the editor flags the precise line-item literal');
  assert.deepEqual(await diagnosticsAfter(connection, 'didChange', {
    textDocument: { uri, version: 3 }, contentChanges: [{ text }],
  }), [], 'restoring the invoice clears its diagnostic');
  await connection.sendNotification('textDocument/didClose', { textDocument });
}

async function modelFields(connection, capabilities) {
  assert.ok(capabilities.completionProvider.triggerCharacters.includes(' '));
  const uri = 'file:///tmp/kscript-smoke/license.kscript';
  const text = 'import KScript.Data\nmodel License(hidden: [key_hash]) {\n  key_hash: string\n  owner_id: string\n}';
  assert.deepEqual(await diagnosticsAfter(connection, 'didOpen', {
    textDocument: { uri, languageId: 'kscript-code', version: 1, text },
  }), []);
  const start = position(text, '])');
  await diagnosticsAfter(connection, 'didChange', {
    textDocument: { uri, version: 2 }, contentChanges: [{ range: { start, end: start }, text: ', ' }],
  });
  const cursor = { line: start.line, character: start.character + 2 };
  const { items } = await connection.sendRequest('textDocument/completion', {
    textDocument: { uri }, position: cursor, context: { triggerKind: 2, triggerCharacter: ' ' },
  });
  assert.ok(items.every((item) => item.label !== 'key_hash'), 'used model fields stay out of the list');
  const owner = items.find((item) => item.label === 'owner_id');
  assert.ok(owner, 'a space after the comma offers the remaining model field');
  assert.deepEqual(owner.textEdit, { newText: 'owner_id', range: { start: cursor, end: cursor } });
  assert.deepEqual(await diagnosticsAfter(connection, 'didChange', {
    textDocument: { uri, version: 3 }, contentChanges: [{ range: owner.textEdit.range, text: owner.textEdit.newText }],
  }), [], 'accepting the completion leaves a valid model');
  await connection.sendNotification('textDocument/didClose', { textDocument: { uri } });
}

function scriptFiles(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const file = path.join(dir, entry.name);
    if (entry.isDirectory()) return scriptFiles(file);
    return file.endsWith('.kscript') || isTestFile(file) ? [file] : [];
  });
}

async function scriptBoundary(connection, runCli) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'kscript-lsp-boundary-'));
  const file = path.join(dir, 'main.kscript');
  const uri = pathToFileURL(file).href;
  const helper = 'fn read_status() { throw Network "offline" }\n';
  const bad = helper + 'echo @read_status()\n';
  const good = helper + 'try { echo @read_status() } catch Network as err { $log->warn($err.message) }\n';
  try {
    fs.writeFileSync(file, bad);
    const diagnostics = await diagnosticsAfter(connection, 'didOpen', {
      textDocument: { uri, languageId: 'kscript-code', version: 1, text: bad },
    });
    assert.equal(diagnostics.length, 1);
    assert.equal(diagnostics[0].code, 'KS5016');
    assert.equal(diagnostics[0].severity, 2);
    assert.equal(diagnostics[0].range.start.line, 1);
    assert.match(diagnostics[0].message, /Network/);
    await assert.rejects(() => runCli(['check', file, '--warnings-as-errors', '--output', 'json']), (err) =>
      err.code === 1 && JSON.parse(err.stdout).problems.some((d) => d.code === 'KS5016'));
    fs.writeFileSync(file, good);
    assert.deepEqual(await diagnosticsAfter(connection, 'didChange', {
      textDocument: { uri, version: 2 }, contentChanges: [{ text: good }],
    }), []);
    await runCli(['check', file, '--warnings-as-errors']);
    const missingAction = "import KScript.Net\n@with_loopback_json(47231, action: null)\n";
    const callbackErrors = await diagnosticsAfter(connection, 'didChange', {
      textDocument: { uri, version: 3 }, contentChanges: [{ text: missingAction }],
    });
    assert.ok(callbackErrors.some((d) => d.code === 'KS5005' && d.severity === 1 &&
      d.range.start.character === position(missingAction, 'null').character));
    assert.deepEqual(await diagnosticsAfter(connection, 'didChange', {
      textDocument: { uri, version: 4 },
      contentChanges: [{ text: missingAction.replace('action: null', 'action: fn(connection) => $connection') }],
    }), []);
    await connection.sendNotification('textDocument/didClose', { textDocument: { uri } });
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

async function discoverCorpus(connection, runCli) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'kscript-lsp-junit-'));
  let results;
  try {
    const report = path.join(dir, 'results.xml');
    await runCli(['test', corpus, '--junit', report]);
    results = parseJUnit(fs.readFileSync(report, 'utf8'));
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
  const files = scriptFiles(corpus).sort();
  const suites = files.filter(isTestFile);
  const { stdout } = await runCli(['check', corpus, '--warnings-as-errors', '--output', 'json']);
  const report = JSON.parse(stdout);
  assert.equal(report.files, files.length, 'CLI and editor enumerate the same scripts and helpers');
  assert.deepEqual(report.problems, [], 'the runnable corpus checks without diagnostics');
  assert.ok(files.length > 0);
  assert.deepEqual([...new Set(results.map((r) => path.resolve(r.file)))].sort(), suites, 'every suite runs');
  for (const file of files) {
    const text = fs.readFileSync(file, 'utf8');
    const uri = pathToFileURL(file).href;
    const textDocument = { uri };
    const diagnostics = await diagnosticsAfter(connection, 'didOpen', {
      textDocument: { uri, languageId: file.endsWith('.kscript') ? 'kscript-code' : 'kscript', version: 1, text },
    });
    assert.deepEqual(diagnostics, [], file + ' opens without diagnostics');
    if (!isTestFile(file)) {
      await connection.sendNotification('textDocument/didClose', { textDocument });
      continue;
    }
    if (path.basename(file) === 'declaration-catches.test.kscript') {
      const hover = await connection.sendRequest('textDocument/hover', {
        textDocument, position: position(text, '@fetch_name($mode)', 4),
      });
      assert.match(hover.contents.value, /May throw/);
      assert.match(hover.contents.value, /Network/);
      assert.match(hover.contents.value, /NotFound/);
      const help = await connection.sendRequest('textDocument/signatureHelp', {
        textDocument, position: position(text, '@fetch_name($mode)', '@fetch_name('.length),
      });
      const docs = help.signatures[0].documentation;
      assert.match(docs.value || docs, /May throw/);
      const declaration = await connection.sendRequest('textDocument/definition', {
        textDocument, position: position(text, '$err.message', 2),
      });
      assert.equal(declaration[0].range.start.line, position(text, '} catch Network | NotFound as err').line);
    }
    if (path.basename(file) === 'extension-methods.test.kscript') {
      const extension = path.join(corpus, 'extensions', 'calendar.kscript');
      const method = position(fs.readFileSync(extension, 'utf8'), 'fn month_key(', 'fn '.length);
      const definitions = await connection.sendRequest('textDocument/definition', {
        textDocument, position: position(text, '$invoice_date->month_key(', '$invoice_date->'.length),
      });
      assert.deepEqual(definitions.map((d) => [d.uri, d.range.start]), [[pathToFileURL(extension).href, method]],
        'Go to Definition opens the month_key declaration in the imported calendar extension');
    }
    if (path.basename(file) === 'dynamic-dispatch.test.kscript') {
      const line = position(text, 'fn trim_dynamic(key: any)').line;
      const hints = await connection.sendRequest('textDocument/inlayHint', {
        textDocument, range: { start: { line, character: 0 }, end: { line: line + 1, character: 0 } },
      });
      assert.ok(!hints.some((h) => h.position.line === line && h.label.trim() === '→ string'),
        'a dynamic receiver can define its own trim return type');
    }
    const { tests } = await connection.sendRequest('kscript/tests', { textDocument });
    const lenses = await connection.sendRequest('textDocument/codeLens', { textDocument });
    assert.ok(lenses.some((l) => l.command.command === 'kscript.runTestsInFile' && l.command.arguments[0] === uri));
    const cases = results.filter((r) => path.resolve(r.file) === file);
    const covered = new Set();
    function check(items) {
      for (const item of items) {
        assert.ok(lenses.some((l) => l.command.command === 'kscript.runTest' && l.command.arguments[0] === uri
          && l.command.arguments[1] === filterFor(item) && l.range.start.line === item.range.start.line),
        'missing Run lens for ' + item.fullName);
        if (item.group) {
          check(item.children);
        } else {
          const pattern = namePattern(item);
          const found = cases.filter((r) => pattern.test(r.name));
          if (!item.template) assert.equal(found.length, 1, 'discovery and JUnit disagree on ' + item.fullName);
          for (const result of found) {
            assert.equal(result.outcome, 'passed', result.name);
            covered.add(result);
          }
        }
      }
    }
    check(tests);
    assert.equal(covered.size, cases.length, file + ' exposes every runtime test in the editor');
    await connection.sendNotification('textDocument/didClose', { textDocument });
  }
  console.log('Language corpus matched ' + results.length + ' JUnit tests across ' + suites.length + ' suites; checked ' + files.length + ' scripts and helpers.');
}

module.exports = async function languageScenarios(connection, capabilities, runCli) {
  await discoverCorpus(connection, runCli);
  await require('./diagnostic-files')(connection, runCli, diagnosticsAfter, position);
  await scriptBoundary(connection, runCli);
  await normalizers(connection);
  await checkoutEdits(connection);
  await modelFields(connection, capabilities);
};
