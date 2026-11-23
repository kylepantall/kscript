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
  fs.writeFileSync(path.join(workspace, 'license.kscript'),
    "import KScript.Data\nmodel License(key: 'key_hash', hidden: [key_hash,]) {\n  key_hash: string\n  owner_id: string\n}\n");
  fs.writeFileSync(path.join(workspace, 'snippets.txt'), '\n');
  // Scripts for the Run command: one that works, one that stops on its second line, and a test file.
  fs.writeFileSync(path.join(workspace, 'writes.kscript'), "import KScript.IO\n@write_text('ran.txt', 'it ran')\n");
  fs.writeFileSync(path.join(workspace, 'stops.kscript'), "stock = 0\nthrow \"out of stock\"\necho $stock\n");
  fs.writeFileSync(path.join(workspace, 'cart.test.kscript'),
    "test(name: \"starts empty\") {\n  expect(value: 0, equals: 0)\n}\ntest(name: \"fails\") {\n  expect(value: 1, equals: 2)\n}\n");
  const languageSamples = path.resolve(__dirname, '../../../../samples/scripts/tests/language');
  fs.mkdirSync(path.join(workspace, 'types'));
  for (const file of ['normalizers.test.kscript', 'types/login-name.kscript', 'declaration-catches.test.kscript', 'types/status-reader.kscript']) {
    fs.copyFileSync(path.join(languageSamples, file), path.join(workspace, file));
  }
  // A kscript serve page: VS Code's HTML grammar must still apply, with KScript { } highlighted on top.
  fs.writeFileSync(path.join(workspace, '_layout.html'), '<!doctype html>\n<html><body><main><slot /></main></body></html>\n');
  fs.writeFileSync(path.join(workspace, 'page.html'), '<layout path="_layout.html" title="Home">\n  <p class="lead">Hi {$title->upper()}</p>\n</layout>\n');

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
