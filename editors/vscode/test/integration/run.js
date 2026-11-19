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

  try {
    await runTests({
      extensionDevelopmentPath: path.resolve(__dirname, '../..'),
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
