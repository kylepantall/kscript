// Runs inside VS Code: opens a script with a typo and checks the language server's diagnostic, completion and hover.
'use strict';

const assert = require('assert');
const path = require('path');
const vscode = require('vscode');

async function waitFor(check, what, timeoutMs = 60000) {
  const start = Date.now();
  for (;;) {
    const value = await check();
    if (value) return value;
    if (Date.now() - start > timeoutMs) throw new Error('Timed out waiting for ' + what);
    await new Promise((r) => setTimeout(r, 250));
  }
}

async function run() {
  const folder = vscode.workspace.workspaceFolders[0].uri.fsPath;
  const doc = await vscode.workspace.openTextDocument(path.join(folder, 'hello.ks.xml'));
  assert.strictEqual(doc.languageId, 'kscript', 'the .ks.xml file is KScript');
  await vscode.window.showTextDocument(doc);

  const diagnostics = await waitFor(() => {
    const d = vscode.languages.getDiagnostics(doc.uri);
    return d.length > 0 ? d : null;
  }, 'diagnostics');
  assert.ok(diagnostics.some((d) => /Did you mean \$total/.test(d.message)), JSON.stringify(diagnostics.map((d) => d.message)));

  const completions = await vscode.commands.executeCommand('vscode.executeCompletionItemProvider', doc.uri, new vscode.Position(2, 12));
  assert.ok(completions.items.some((i) => (i.label.label || i.label) === 'total'), 'variables are completed');

  const hovers = await vscode.commands.executeCommand('vscode.executeHoverProvider', doc.uri, new vscode.Position(1, 4));
  assert.ok(hovers.length > 0, 'hover on <let>');

  console.log('VS Code integration test passed');
}

module.exports = { run };
