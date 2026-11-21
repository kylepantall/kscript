// Runs the extension inside a real VS Code (downloaded by @vscode/test-electron) against the built server.
// Usage: node test/integration/run.js <path to kscript executable or KScript.dll>
'use strict';

const path = require('path');
const fs = require('fs');
const os = require('os');
const { runTests } = require('@vscode/test-electron');

(async () => {
  const server = path.resolve(process.argv[2] || path.join(__dirname, '../../../../KScriptApp/bin/Release/net8.0/KScript.dll'));
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'kscript-vscode-'));
  fs.mkdirSync(path.join(workspace, '.vscode'));
  fs.writeFileSync(path.join(workspace, '.vscode', 'settings.json'), JSON.stringify({ 'kscript.server.path': server }));
  fs.writeFileSync(path.join(workspace, 'hello.ks.xml'), '<kscript>\n  <let name="total" value="1" />\n  <echo>{ $totl }</echo>\n</kscript>\n');
  fs.writeFileSync(path.join(workspace, 'snippets.ks.xml'), '<kscript>\n\n</kscript>\n');
  fs.writeFileSync(path.join(workspace, 'snippets.kscript'), '\n');
  fs.writeFileSync(path.join(workspace, 'snippets.txt'), '\n');
  // A kscript serve page: VS Code's HTML grammar must still apply, with KScript { } highlighted on top.
  fs.writeFileSync(path.join(workspace, '_layout.html'), '<!doctype html>\n<html><body><main><slot /></main></body></html>\n');
  fs.writeFileSync(path.join(workspace, 'page.html'), '<layout src="_layout.html" title="Home">\n  <p class="lead">Hi {$title->upper()}</p>\n</layout>\n');

  try {
    await runTests({
      // The second extension contributes a snippet to every language, like 1Password's op:// one.
      extensionDevelopmentPath: [path.resolve(__dirname, '../..'), path.resolve(__dirname, '../fixtures/global-snippet')],
      extensionTestsPath: path.resolve(__dirname, 'suite.js'),
      launchArgs: [workspace, '--disable-extensions', '--disable-workspace-trust'],
    });
  } catch (err) {
    console.error('Integration tests failed:', err);
    process.exit(1);
  } finally {
    fs.rmSync(workspace, { recursive: true, force: true });
  }
})();
