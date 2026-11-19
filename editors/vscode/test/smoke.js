// End-to-end check with the same JSON-RPC library VS Code's client uses: start `kscript lsp`, open a script,
// and check diagnostics, completion, hover and definition. Usage: node test/smoke.js <path to KScript.dll>
'use strict';

const assert = require('assert');
const cp = require('child_process');
const path = require('path');
const rpc = require('vscode-jsonrpc/node');

const dll = process.argv[2] || path.resolve(__dirname, '../../../KScriptApp/bin/Debug/net8.0/KScript.dll');
const server = cp.spawn('dotnet', [dll, 'lsp'], { stdio: ['pipe', 'pipe', 'inherit'] });
const connection = rpc.createMessageConnection(new rpc.StreamMessageReader(server.stdout), new rpc.StreamMessageWriter(server.stdin));

const uri = 'file:///tmp/kscript-smoke/hello.ks.xml';
const text = [
  '<kscript>',
  '  <let name="total" value="1" />',
  '  <echo>{ $totl }</echo>',
  '  <echo>{ $total-> }</echo>',
  '</kscript>',
].join('\n');

const diagnostics = new Promise((resolve) => {
  connection.onNotification('textDocument/publishDiagnostics', (p) => { if (p.uri === uri) resolve(p.diagnostics); });
});

(async () => {
  connection.listen();
  const init = await connection.sendRequest('initialize', { processId: process.pid, rootUri: null, capabilities: {} });
  assert.ok(init.capabilities.completionProvider, 'completion capability');
  assert.ok(init.capabilities.semanticTokensProvider.legend.tokenTypes.includes('variable'));
  connection.sendNotification('initialized', {});
  connection.sendNotification('textDocument/didOpen', { textDocument: { uri, languageId: 'kscript', version: 1, text } });

  const diags = await diagnostics;
  assert.ok(diags.some((d) => d.code === 'KS4001' && /Did you mean \$total/.test(d.message)), 'undefined variable diagnostic: ' + JSON.stringify(diags));

  const completion = await connection.sendRequest('textDocument/completion', { textDocument: { uri }, position: { line: 3, character: 18 } });
  assert.ok(completion.items.some((i) => i.label === 'round'), 'number methods offered after ->');

  const hover = await connection.sendRequest('textDocument/hover', { textDocument: { uri }, position: { line: 1, character: 4 } });
  assert.ok(/let/.test(hover.contents.value), 'hover on <let>');

  const definition = await connection.sendRequest('textDocument/definition', { textDocument: { uri }, position: { line: 3, character: 12 } });
  assert.strictEqual(definition[0].range.start.line, 1, 'definition of $total');

  const tokens = await connection.sendRequest('textDocument/semanticTokens/full', { textDocument: { uri } });
  assert.ok(tokens.data.length > 0 && tokens.data.length % 5 === 0, 'semantic tokens');

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
