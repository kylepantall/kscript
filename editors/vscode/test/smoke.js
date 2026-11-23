// End-to-end check with the same JSON-RPC library VS Code's client uses: start `kscript lsp`, open a script,
// and check diagnostics, completion, hover and definition. Usage: node test/smoke.js <path to KScript.dll or a kscript binary>
'use strict';

const assert = require('assert');
const cp = require('child_process');
const path = require('path');
const execFile = require('util').promisify(cp.execFile);
const rpc = require('vscode-jsonrpc/node');

const dll = process.argv[2] || path.resolve(__dirname, '../../../KScriptApp/bin/Debug/net8.0/KScript.dll');
// A .dll runs with dotnet; anything else is a kscript binary (single-file or Native AOT) and runs as it is.
const executable = dll.endsWith('.dll') ? 'dotnet' : dll;
const cliArgs = dll.endsWith('.dll') ? [dll] : [];
const server = cp.spawn(executable, [...cliArgs, 'lsp'], { stdio: ['pipe', 'pipe', 'inherit'] });
const connection = rpc.createMessageConnection(new rpc.StreamMessageReader(server.stdout), new rpc.StreamMessageWriter(server.stdin));

const uri = 'file:///tmp/kscript-smoke/hello.ks.xml';
const text = [
  '<kscript>',
  '  <let name="total" value="1" />',
  '  <echo>{ $totl }</echo>',
  '  <echo>{ $total-> }</echo>',
  '  <!-- Functions -->',
  '  <fn name="double" params="x: int" value="$x * 2" />',
  '  <let name="d" value="@double($total)" />',
  '</kscript>',
].join('\n');

const diagnostics = new Promise((resolve) => {
  connection.onNotification('textDocument/publishDiagnostics', (p) => { if (p.uri === uri) resolve(p.diagnostics); });
});

(async () => {
  connection.listen();
  const init = await connection.sendRequest('initialize', { processId: process.pid, rootUri: null, capabilities: {}, initializationOptions: { codeLens: true } });
  assert.ok(init.capabilities.codeLensProvider, 'code lenses are offered to an editor that runs them');
  assert.ok(init.capabilities.completionProvider, 'completion capability');
  assert.ok(init.capabilities.semanticTokensProvider.legend.tokenTypes.includes('variable'));
  connection.sendNotification('initialized', {});
  connection.sendNotification('textDocument/didOpen', { textDocument: { uri, languageId: 'kscript', version: 1, text } });

  const diags = await diagnostics;
  assert.ok(diags.some((d) => d.code === 'KS4001' && /Did you mean \$total/.test(d.message)), 'undefined variable diagnostic: ' + JSON.stringify(diags));
  assert.ok(diags.every((d) => d.codeDescription.href.endsWith('/docs/errors.md#' + d.code.toLowerCase())), 'each code links to its explanation');

  const completion = await connection.sendRequest('textDocument/completion', { textDocument: { uri }, position: { line: 3, character: 18 } });
  assert.ok(completion.items.some((i) => i.label === 'round'), 'number methods offered after ->');

  const hover = await connection.sendRequest('textDocument/hover', { textDocument: { uri }, position: { line: 1, character: 4 } });
  assert.ok(/let/.test(hover.contents.value), 'hover on <let>');

  const definition = await connection.sendRequest('textDocument/definition', { textDocument: { uri }, position: { line: 3, character: 12 } });
  assert.strictEqual(definition[0].range.start.line, 1, 'definition of $total');

  const tokens = await connection.sendRequest('textDocument/semanticTokens/full', { textDocument: { uri } });
  assert.ok(tokens.data.length > 0 && tokens.data.length % 5 === 0, 'semantic tokens');

  const hints = await connection.sendRequest('textDocument/inlayHint', { textDocument: { uri }, range: { start: { line: 0, character: 0 }, end: { line: 8, character: 0 } } });
  assert.ok(hints.some((h) => h.label === ': number' && h.position.line === 6), 'inlay type hint for $d: ' + JSON.stringify(hints));
  assert.ok(hints.some((h) => h.label === ' → number' && h.position.line === 5), 'inlay return type for @double');

  const blocks = await connection.sendRequest('kscript/blocks', { textDocument: { uri } });
  assert.deepStrictEqual(blocks.blocks.map((b) => b.label), ['let total', 'Functions'], 'notebook-style blocks');

  const folds = await connection.sendRequest('textDocument/foldingRange', { textDocument: { uri } });
  assert.ok(folds.some((f) => f.startLine === 0), 'the script folds');

  const lenses = await connection.sendRequest('textDocument/codeLens', { textDocument: { uri } });
  assert.deepStrictEqual(lenses.map((l) => [l.command.title, l.command.command, l.command.arguments[0], l.range.start.line]), [['▶ Run', 'kscript.run', uri, 1], ['1 use', 'kscript.showReferences', uri, 5]], 'a Run lens on the first statement and a count of uses on the function');

  const symbols = await connection.sendRequest('workspace/symbol', { query: 'doub' });
  assert.deepStrictEqual(symbols.map((f) => f.name), ['double'], 'a function is found by part of its name');

  // A new, unsaved .kscript file is checked as code, and a test file lists its tests.
  const untitled = 'untitled:Untitled-1';
  const untitledDiagnostics = new Promise((resolve) => {
    connection.onNotification('textDocument/publishDiagnostics', (p) => { if (p.uri === untitled) resolve(p.diagnostics); });
  });
  connection.sendNotification('textDocument/didOpen', { textDocument: { uri: untitled, languageId: 'kscript-code', version: 1, text: "name = 'Joan'\necho \"Hello $nmae\"\n" } });
  assert.ok((await untitledDiagnostics).some((d) => d.code === 'KS4001' && /Did you mean \$name/.test(d.message)), 'an untitled .kscript file is checked');

  // The script New Script opens has nothing to fix, and can be run.
  const starter = 'untitled:Untitled-2';
  const starterDiagnostics = new Promise((resolve) => {
    connection.onNotification('textDocument/publishDiagnostics', (p) => { if (p.uri === starter) resolve(p.diagnostics); });
  });
  connection.sendNotification('textDocument/didOpen', { textDocument: { uri: starter, languageId: 'kscript-code', version: 1, text: require('../src/starter').STARTER } });
  assert.deepStrictEqual(await starterDiagnostics, [], 'the starter script has no problems');
  const starterLenses = await connection.sendRequest('textDocument/codeLens', { textDocument: { uri: starter } });
  assert.deepStrictEqual(starterLenses.map((l) => [l.command.title, l.range.start.line]), [['▶ Run', 1], ['1 use', 5]], 'the starter script can be run from its first statement');

  const testUri = 'file:///tmp/kscript-smoke/cart.test.kscript';
  connection.sendNotification('textDocument/didOpen', { textDocument: { uri: testUri, languageId: 'kscript-code', version: 1, text: 'describe(name: "cart") {\n  test(name: "starts empty") {\n    expect(value: 0, equals: 0)\n  }\n}\n' } });
  const tests = await connection.sendRequest('kscript/tests', { textDocument: { uri: testUri } });
  assert.strictEqual(tests.tests[0].children[0].fullName, 'cart › starts empty', 'the tests of a test file');
  const testLenses = await connection.sendRequest('textDocument/codeLens', { textDocument: { uri: testUri } });
  assert.deepStrictEqual(testLenses.map((l) => l.command.command), ['kscript.runTestsInFile', 'kscript.runTest', 'kscript.runTest'], 'Run test lenses');

  await require('./language-scenarios')(connection, init.capabilities,
    (args) => execFile(executable, [...cliArgs, ...args], { timeout: 60000 }));
  await require('./interpolation')(connection, init.capabilities);

  await connection.sendRequest('shutdown');
  connection.sendNotification('exit');
  server.on('exit', (code) => {
    assert.strictEqual(code, 0, 'clean exit');
    console.log('smoke test passed');
    process.exit(0);
  });
})().catch((err) => {
  console.error(err);
  server.kill();
  process.exit(1);
});
