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

// Types "op" on the first empty line, opens the suggest widget and accepts the top suggestion.
// Returns the line afterwards, so op://vault/item/field means another extension's snippet was offered.
async function typeAndAccept(folder, file) {
  const doc = await vscode.workspace.openTextDocument(path.join(folder, file));
  const editor = await vscode.window.showTextDocument(doc);
  const line = doc.getText().split('\n').findIndex((l) => l.trim() === '');
  editor.selection = new vscode.Selection(line, 0, line, 0);
  await vscode.commands.executeCommand('type', { text: 'op' });
  await vscode.commands.executeCommand('editor.action.triggerSuggest');
  await new Promise((r) => setTimeout(r, 3000));
  await vscode.commands.executeCommand('acceptSelectedSuggestion');
  await new Promise((r) => setTimeout(r, 500));
  const text = doc.lineAt(line).text;
  await vscode.commands.executeCommand('workbench.action.files.revert');
  return { languageId: doc.languageId, text };
}

async function snippetsFromOtherExtensions(folder) {
  const effective = (languageId) => {
    const editor = vscode.workspace.getConfiguration('editor', { languageId });
    return { snippetSuggestions: editor.get('snippetSuggestions'), showSnippets: editor.get('suggest.showSnippets') };
  };
  console.log('effective settings', JSON.stringify({ kscript: effective('kscript'), 'kscript-code': effective('kscript-code') }));

  // Control: in a plain text file the fixture's snippet is offered.
  const plain = await typeAndAccept(folder, 'snippets.txt');
  console.log('plaintext', JSON.stringify(plain));
  assert.match(plain.text, /op:\/\//, 'the fixture snippet works in plain text: ' + JSON.stringify(plain));

  for (const file of ['snippets.ks.xml', 'snippets.kscript']) {
    const result = await typeAndAccept(folder, file);
    console.log(file, JSON.stringify(result));
    assert.doesNotMatch(result.text, /op:\/\//, `other extensions' snippets are not offered in ${file}: ` + JSON.stringify(result));
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

  await snippetsFromOtherExtensions(folder);

  console.log('VS Code integration test passed');
}

module.exports = { run };
