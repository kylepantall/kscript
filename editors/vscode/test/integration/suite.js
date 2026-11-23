// Runs inside VS Code: opens a script with a typo and checks the language server's diagnostic, completion and hover.
'use strict';

const assert = require('assert');
const fs = require('fs');
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

// Run above a script, running it, and the line it stopped on marked as an error.
async function runsScripts(folder) {
  const commands = await vscode.commands.getCommands(true);
  for (const id of ['kscript.run', 'kscript.runTest', 'kscript.runTestsInFile', 'kscript.serve', 'kscript.check', 'kscript.formatProject', 'kscript.newProject', 'kscript.newScript', 'kscript.openDocs', 'kscript.showReferences']) {
    assert.ok(commands.includes(id), `${id} is registered`);
  }

  const ok = await vscode.workspace.openTextDocument(path.join(folder, 'writes.kscript'));
  assert.strictEqual(ok.languageId, 'kscript-code', 'the .kscript file is KScript');
  await vscode.window.showTextDocument(ok);
  const lenses = await waitFor(async () => {
    const found = await vscode.commands.executeCommand('vscode.executeCodeLensProvider', ok.uri);
    return found && found.length > 0 ? found : null;
  }, 'the Run lens');
  assert.deepStrictEqual(lenses.map((l) => [l.command.title, l.command.command]), [['▶ Run', 'kscript.run']]);

  assert.strictEqual(await vscode.commands.executeCommand('kscript.run', ok.uri), 0, 'the script ran');
  assert.strictEqual(fs.readFileSync(path.join(folder, 'ran.txt'), 'utf8').trim(), 'it ran', 'the script wrote its file');

  const bad = await vscode.workspace.openTextDocument(path.join(folder, 'stops.kscript'));
  await vscode.window.showTextDocument(bad);
  assert.strictEqual(await vscode.commands.executeCommand('kscript.run', bad.uri.toString()), 3, 'a script that stops with an error exits with 3');
  const stopped = await waitFor(() => vscode.languages.getDiagnostics(bad.uri).find((d) => /out of stock/.test(d.message)), 'the run-time error on its line');
  assert.strictEqual(stopped.range.start.line, 1, 'the error is on the line that threw: ' + JSON.stringify(stopped));
  assert.strictEqual(stopped.severity, vscode.DiagnosticSeverity.Error);

  // New Script opens a script that is ready to run, with the first thing to change selected.
  const fresh = await vscode.commands.executeCommand('kscript.newScript');
  assert.strictEqual(fresh.languageId, 'kscript-code', 'the new script is KScript');
  assert.ok(fresh.isUntitled && fresh.getText().includes('echo "Hello {$name}!"'), 'the new script has the starter text');
  assert.strictEqual(fresh.getText(vscode.window.activeTextEditor.selection), 'world', 'the first thing to change is selected');
  await vscode.commands.executeCommand('workbench.action.revertAndCloseActiveEditor');

  // A test file gets a lens for each test, and running one test passes.
  const tests = await vscode.workspace.openTextDocument(path.join(folder, 'cart.test.kscript'));
  await vscode.window.showTextDocument(tests);
  const testLenses = await waitFor(async () => {
    const found = await vscode.commands.executeCommand('vscode.executeCodeLensProvider', tests.uri);
    return found && found.length > 0 ? found : null;
  }, 'the Run test lenses');
  assert.deepStrictEqual(testLenses.map((l) => l.command.command), ['kscript.runTestsInFile', 'kscript.runTest', 'kscript.runTest']);
  const one = testLenses[1].command;
  assert.strictEqual(await vscode.commands.executeCommand(one.command, ...one.arguments), 0, 'the passing test passes');
  assert.strictEqual(await vscode.commands.executeCommand('kscript.runTestsInFile', tests.uri), 1, 'the file has a failing test');
}

// Exercise the same refinement through the language client, the file checker, and the test runner.
async function normalizerEdits(folder) {
  const doc = await vscode.workspace.openTextDocument(path.join(folder, 'normalizers.test.kscript'));
  const editor = await vscode.window.showTextDocument(doc);
  const at = (text, offset = 0) => {
    const index = doc.getText().indexOf(text);
    assert.ok(index >= 0, 'fixture contains ' + text);
    return doc.positionAt(index + offset);
  };
  const rangeOf = (text) => new vscode.Range(at(text), at(text, text.length));
  const command = (name) => vscode.commands.executeCommand(name, doc.uri);
  const labelsAt = async (position) => {
    const list = await vscode.commands.executeCommand('vscode.executeCompletionItemProvider', doc.uri, position);
    return list.items.map((item) => item.label.label || item.label);
  };
  const checkAndRun = async () => {
    assert.strictEqual(await command('kscript.checkFile'), 0, 'Check This File accepts the current normalizer');
    assert.strictEqual(await command('kscript.run'), 0, 'Run executes the tests on the current normalizer');
  };

  const helperUse = at('@trim_key($value)', 3);
  assert.ok((await labelsAt(helperUse)).includes('trim_key'), 'the later local helper is offered in the normalizer');
  const moduleFunctions = await labelsAt(at('@key_size(', 1));
  assert.ok(moduleFunctions.includes('key_size'), 'the module function is offered outside the refinement');
  assert.ok(!moduleFunctions.includes('trim_key'), 'the member stays out of module completions');
  const definitions = await vscode.commands.executeCommand('vscode.executeDefinitionProvider', doc.uri, helperUse);
  assert.strictEqual(definitions.length, 1);
  const definition = definitions[0].targetSelectionRange || definitions[0].range;
  assert.strictEqual(doc.getText(definition), 'trim_key');
  assert.ok(definition.start.isEqual(at('fn trim_key(', 3)), 'Go to Definition selects the member declaration');
  const hovers = await vscode.commands.executeCommand('vscode.executeHoverProvider', doc.uri, helperUse);
  assert.match(hovers.flatMap((h) => h.contents.map((c) => c.value || c)).join('\n'), /trim_key\(key: string\)/);
  await checkAndRun();

  const rename = await vscode.commands.executeCommand('vscode.executeDocumentRenameProvider', doc.uri, helperUse, 'clean_key');
  const edits = rename.get(doc.uri);
  assert.deepStrictEqual(edits.map((e) => doc.getText(e.range)), ['trim_key', 'trim_key', 'trim_key']);
  assert.ok(await vscode.workspace.applyEdit(rename), 'Rename updates the declaration, normalizer call and receiver call');
  assert.ok(await editor.edit((edit) => edit.replace(rangeOf('@clean_key($value)'), '@clean_key')));
  await checkAndRun();

  // A real edit prompts diagnostics. Ask the provider for the fix, apply it, then run the restored document.
  assert.ok(await editor.edit((edit) => edit.replace(rangeOf('@clean_key'), '@clean_keey')));
  const unknown = (editorOnly = false) => vscode.languages.getDiagnostics(doc.uri).find((d) =>
    (!editorOnly || (d.code && d.code.target)) && String((d.code && d.code.value) || d.code) === 'KS5001' && d.message.includes('@clean_keey'));
  // Language-server diagnostics link their code to its documentation. The CLI task matcher uses a plain code.
  const diagnostic = await waitFor(() => unknown(true), 'live diagnostic for the unknown normalizer helper');
  assert.strictEqual(await command('kscript.checkFile'), 1, 'Check This File rejects the unknown helper');
  assert.ok((await command('kscript.run')) > 0, 'the test runner also rejects the unknown helper');
  const actions = await vscode.commands.executeCommand('vscode.executeCodeActionProvider', doc.uri, diagnostic.range, 'quickfix');
  const fix = actions.find((a) => a.edit && a.edit.get(doc.uri).some((e) => e.newText === 'clean_key'));
  assert.ok(fix, 'the quick fix offers the scoped helper');
  assert.ok(await vscode.workspace.applyEdit(fix.edit));
  assert.ok(doc.getText().includes('normalize: @clean_key\n'), 'the fix preserves the callback reference');
  await waitFor(() => !unknown(true), 'the live language-server diagnostic to clear after the edit');
  await checkAndRun();
  await waitFor(() => !unknown(), 'the fixed diagnostic to disappear from both the editor and file checker');
}

async function interpolatedLinks(folder) {
  const { writeFixture, tokensOnLine } = require('../interpolation');
  const { file, settings, lines } = writeFixture(fs.mkdtempSync(path.join(folder, 'interpolation-')));
  const doc = await vscode.workspace.openTextDocument(file);
  await vscode.window.showTextDocument(doc);
  const syntax = await vscode.commands.executeCommand('_workbench.captureSyntaxTokens', doc.uri);
  assert.ok(syntax.some(t => t.c === 'passkeys' && /meta\.embedded\.expression.*variable\.other\.property/.test(t.t)),
    'VS Code colors the setting as a property inside the URL expression');
  const legend = await vscode.commands.executeCommand('vscode.provideDocumentSemanticTokensLegend', doc.uri);
  const semantic = await vscode.commands.executeCommand('vscode.provideDocumentSemanticTokens', doc.uri);
  const tokens = tokensOnLine(semantic.data, legend.tokenTypes, 1, lines[1]);
  for (const [text, type] of [['$kind', 'parameter'], ['url', 'property'], ['/finish', 'string']]) {
    assert.ok(tokens.some(t => t.text === text && t.type === type), 'VS Code semantic highlighting for ' + text);
  }

  for (const [anchor, destination, line, column] of [
    ['$kind', file, 1, lines[1].indexOf('kind:')],
    ['.url', settings, 1, 2],
  ]) {
    const position = new vscode.Position(1, lines[1].indexOf(anchor) + 2);
    const found = await vscode.commands.executeCommand('vscode.executeDefinitionProvider', doc.uri, position);
    assert.ok(found.some(loc => (loc.uri || loc.targetUri).fsPath === destination &&
      (loc.range || loc.targetSelectionRange).start.isEqual(new vscode.Position(line, column))), 'Go to Definition on ' + anchor);
  }
}

async function dispatchWorkflow(folder) {
  const doc = await vscode.workspace.openTextDocument(path.join(folder, 'dispatch-workflow.test.kscript'));
  await vscode.window.showTextDocument(doc);
  const call = doc.getText().indexOf('->dispatch(');
  assert.ok(call >= 0);
  const definitions = await vscode.commands.executeCommand('vscode.executeDefinitionProvider', doc.uri, doc.positionAt(call + 4));
  assert.strictEqual(definitions.length, 1);
  const target = definitions[0];
  const policy = await vscode.workspace.openTextDocument(target.targetUri || target.uri);
  assert.strictEqual(policy.uri.fsPath, path.join(folder, 'types', 'dispatch-policy.kscript'));
  const range = target.targetSelectionRange || target.range;
  assert.strictEqual(policy.getText(range), 'dispatch');
  assert.strictEqual(await vscode.commands.executeCommand('kscript.runTestsInFile', doc.uri), 0);
}

async function licenseGuardWarnings(folder) {
  const doc = await vscode.workspace.openTextDocument(path.join(folder, 'tools', 'issue-license.kscript'));
  await vscode.window.showTextDocument(doc);
  const warnings = await waitFor(() => {
    const problems = vscode.languages.getDiagnostics(doc.uri);
    return problems.length === 3 && problems;
  }, 'three license guard warnings in Problems');
  assert.deepStrictEqual(warnings.map((d) => {
    assert.strictEqual(d.severity, vscode.DiagnosticSeverity.Warning);
    assert.strictEqual(doc.getText(d.range), 'if');
    assert.match(d.message, /Validation/);
    return [d.code.value, d.range.start.line + 1];
  }).sort((a, b) => a[1] - b[1]), [['KS5016', 3], ['KS5016', 6], ['KS5016', 9]]);
}

async function declarationHandlers(folder) {
  const doc = await vscode.workspace.openTextDocument(path.join(folder, 'declaration-catches.test.kscript'));
  await vscode.window.showTextDocument(doc);
  const call = doc.getText().indexOf('@fetch_name($mode)');
  assert.ok(call >= 0);
  const hovers = await vscode.commands.executeCommand('vscode.executeHoverProvider', doc.uri, doc.positionAt(call + 3));
  const info = hovers.flatMap((h) => h.contents.map((c) => c.value || c)).join('\n');
  assert.match(info, /May throw/);
  assert.match(info, /Network/);
  assert.match(info, /NotFound/);
  assert.strictEqual(await vscode.commands.executeCommand('kscript.checkFile', doc.uri), 0);
  assert.strictEqual(await vscode.commands.executeCommand('kscript.runTestsInFile', doc.uri), 0);
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

  // Type a space after the comma and accept the field without manually opening the suggest widget.
  const model = await vscode.workspace.openTextDocument(path.join(folder, 'license.kscript'));
  const editor = await vscode.window.showTextDocument(model);
  const nextField = model.lineAt(1).text.indexOf(']');
  editor.selection = new vscode.Selection(1, nextField, 1, nextField);
  await vscode.commands.executeCommand('type', { text: ' ' });
  await waitFor(async () => {
    await vscode.commands.executeCommand('acceptSelectedSuggestion');
    return model.lineAt(1).text.includes('[key_hash, owner_id]');
  }, 'automatic field suggestions after a comma and space');

  // An explicit file check works without a kscript.json and returns the CLI result.
  assert.strictEqual(await vscode.commands.executeCommand('kscript.checkFile', doc.uri), 1, 'the typo fails Check This File');
  const correct = await vscode.workspace.openTextDocument(path.join(folder, 'writes.kscript'));
  assert.strictEqual(await vscode.commands.executeCommand('kscript.checkFile', correct.uri), 0, 'a valid file passes');

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
  await runsScripts(folder);
  await normalizerEdits(folder);
  await declarationHandlers(folder);
  await licenseGuardWarnings(folder);
  await dispatchWorkflow(folder);
  await interpolatedLinks(folder);

  console.log('VS Code integration test passed');
}

module.exports = { run };
