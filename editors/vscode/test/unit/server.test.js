'use strict';

const test = require('node:test');
const assert = require('node:assert');
const path = require('path');
const fs = require('fs');
const os = require('os');
const { installLocations, findOnPath, expand, resolveServer, supportsLsp, versionOf } = require('../../src/server');
const { isSiteFolder } = require('../../src/site');

const only = (...files) => (p) => files.includes(p);

test('install.sh location comes first on macOS and Linux', () => {
  assert.deepStrictEqual(installLocations({}, 'linux', '/home/k'), ['/home/k/.kscript/bin/kscript']);
  assert.deepStrictEqual(installLocations({ KSCRIPT_INSTALL: '/opt/ks' }, 'darwin', '/Users/k'), ['/opt/ks/bin/kscript', '/Users/k/.kscript/bin/kscript']);
});

test('install.ps1 location is used on Windows', () => {
  const env = { LOCALAPPDATA: 'C:\\Users\\k\\AppData\\Local' };
  assert.strictEqual(installLocations(env, 'win32', 'C:\\Users\\k')[0], 'C:\\Users\\k\\AppData\\Local\\Programs\\kscript\\kscript.exe');
});

test('the installed binary wins over PATH', () => {
  const r = resolveServer('', ['lsp'], { env: { PATH: '/usr/bin' }, platform: 'linux', home: '/home/k', exists: only('/home/k/.kscript/bin/kscript', '/usr/bin/kscript') });
  assert.deepStrictEqual(r, { command: '/home/k/.kscript/bin/kscript', args: ['lsp'], source: 'install' });
});

test('falls back to PATH', () => {
  const r = resolveServer('', ['lsp'], { env: { PATH: '/a:/usr/local/bin' }, platform: 'darwin', home: '/Users/k', exists: only('/usr/local/bin/kscript') });
  assert.strictEqual(r.command, '/usr/local/bin/kscript');
  assert.strictEqual(r.source, 'path');
});

test('finds kscript.exe on the Windows PATH', () => {
  const env = { Path: 'C:\\tools;C:\\ks', PATHEXT: '.COM;.EXE' };
  assert.strictEqual(findOnPath({ PATH: env.Path, PATHEXT: env.PATHEXT }, 'win32', only('C:\\ks\\kscript.exe')), 'C:\\ks\\kscript.exe');
});

test('the setting overrides everything, with ~ and ${env:} expanded', () => {
  const opts = { env: { KS: '/x' }, platform: 'linux', home: '/home/k', exists: only('/home/k/dev/kscript', '/x/KScript.dll', '/home/k/.kscript/bin/kscript') };
  assert.strictEqual(resolveServer('~/dev/kscript', ['lsp'], opts).command, '/home/k/dev/kscript');
  assert.deepStrictEqual(resolveServer('${env:KS}/KScript.dll', ['lsp'], opts), { command: 'dotnet', args: ['/x/KScript.dll', 'lsp'], source: 'setting' });
  assert.match(resolveServer('/nope/kscript', ['lsp'], opts).error, /doesn't exist/);
  assert.strictEqual(expand('~', {}, '/h'), '/h');
});

test('reports a clear error when KScript is not installed', () => {
  const r = resolveServer('', ['lsp'], { env: { PATH: '/usr/bin' }, platform: 'linux', home: '/home/k', exists: () => false });
  assert.match(r.error, /isn't installed/);
});

test('versions with kscript lsp', () => {
  assert.ok(supportsLsp('0.1.0'));
  assert.ok(supportsLsp('1.2.3'));
  assert.ok(supportsLsp('0.0.0-dev'));
  assert.ok(!supportsLsp('0.0.9'));
  assert.ok(!supportsLsp(null));
  assert.strictEqual(versionOf({ command: 'kscript', args: ['lsp'] }, () => '0.1.0\n'), '0.1.0');
  assert.strictEqual(versionOf({ command: 'missing', args: ['lsp'] }, () => { throw new Error('ENOENT'); }), null);
});

test('detects kscript serve sites', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ks-site-'));
  try {
    assert.strictEqual(await isSiteFolder(dir), false);
    fs.mkdirSync(path.join(dir, 'web', '_components'), { recursive: true });
    assert.strictEqual(await isSiteFolder(dir), true);
    assert.strictEqual(await isSiteFolder(dir, 0), false);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('the manifest is consistent', () => {
  const pkg = require('../../package.json');
  for (const g of pkg.contributes.grammars) {
    assert.ok(fs.existsSync(path.join(__dirname, '../..', g.path)), g.path);
    JSON.parse(fs.readFileSync(path.join(__dirname, '../..', g.path), 'utf8'));
  }
  assert.ok(fs.existsSync(path.join(__dirname, '../..', pkg.icon)));
  const used = fs.readFileSync(path.join(__dirname, '../../extension.js'), 'utf8');
  for (const c of pkg.contributes.commands) {
    assert.ok(used.includes(`'${c.command}'`), `${c.command} is registered`);
  }
});
