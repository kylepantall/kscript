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
  // $total is only read through the typo, so it is unused: faded, with the Unnecessary tag.
  const unused = diagnostics.find((d) => String((d.code && d.code.value) || d.code) === 'KS6003');
  assert.ok(unused && (unused.tags || []).includes(vscode.DiagnosticTag.Unnecessary), JSON.stringify(diagnostics));

  const completions = await vscode.commands.executeCommand('vscode.executeCompletionItemProvider', doc.uri, new vscode.Position(2, 12));
  assert.ok(completions.items.some((i) => (i.label.label || i.label) === 'total'), 'variables are completed');

  const hovers = await vscode.commands.executeCommand('vscode.executeHoverProvider', doc.uri, new vscode.Position(1, 4));
  assert.ok(hovers.length > 0, 'hover on <let>');

  // .html pages keep VS Code's HTML language and grammar; KScript only adds { } expressions on top.
  const page = await vscode.workspace.openTextDocument(path.join(folder, 'page.html'));
  assert.strictEqual(page.languageId, 'html', 'a page stays HTML');
  await vscode.window.showTextDocument(page);
  const tokens = await vscode.commands.executeCommand('_workbench.captureSyntaxTokens', page.uri);
  const scopeOf = (text) => (tokens.find((t) => t.c === text) || {}).t || '';
  assert.match(scopeOf('p'), /entity\.name\.tag\.html/, 'HTML tags are highlighted: ' + JSON.stringify(tokens));
  assert.match(scopeOf('class'), /entity\.other\.attribute-name\.html/, 'HTML attributes are highlighted');
  assert.match(scopeOf('$title'), /variable\.other\.kscript/, 'KScript expressions are highlighted in pages');
  const layout = await vscode.workspace.openTextDocument(path.join(folder, '_layout.html'));
  await vscode.window.showTextDocument(layout);
  const layoutTokens = await vscode.commands.executeCommand('_workbench.captureSyntaxTokens', layout.uri);
  assert.ok(!layoutTokens.some((t) => /invalid\.illegal/.test(t.t)), '<slot /> is not marked as an error: ' + JSON.stringify(layoutTokens));

  console.log('VS Code integration test passed');
}

module.exports = { run };
