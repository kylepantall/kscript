'use strict';

const test = require('node:test');
const assert = require('node:assert');
const path = require('path');
const fs = require('fs');
const os = require('os');
const { installLocations, findOnPath, expand, resolveServer, supportsLsp, versionOf, isOlderThan } = require('../../src/server');
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
  const ignore = fs.readFileSync(path.join(__dirname, '../../.vscodeignore'), 'utf8');
  for (const id of ['kscript', 'kscript-code']) {
    const lang = pkg.contributes.languages.find((l) => l.id === id);
    for (const theme of ['light', 'dark']) {
      const icon = lang.icon && lang.icon[theme];
      assert.ok(icon && fs.existsSync(path.join(__dirname, '../..', icon)), `${id} has a ${theme} file icon`);
      assert.match(ignore, new RegExp('^!' + path.dirname(icon).replace('./', '') + '/\\*\\*$', 'm'), `${icon} is packaged`);
    }
  }
  const used = fs.readFileSync(path.join(__dirname, '../../extension.js'), 'utf8');
  for (const c of pkg.contributes.commands) {
    assert.ok(used.includes(`'${c.command}'`), `${c.command} is registered`);
  }
});

test('page highlighting is injected into the grammar VS Code uses for .html files', () => {
  const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '../../package.json'), 'utf8'));
  const injection = pkg.contributes.grammars.find((g) => g.scopeName === 'kscript.pages.injection');
  // VS Code opens .html with text.html.derivative, which embeds text.html.basic: both must get the injection.
  assert.ok(injection.injectTo.includes('text.html.derivative'));
  assert.ok(injection.injectTo.includes('text.html.basic'));
  const grammar = JSON.parse(fs.readFileSync(path.join(__dirname, '../..', injection.path), 'utf8'));
  assert.match(grammar.injectionSelector, /^L:text\.html -source/, 'the selector matches both HTML grammars and skips <script>/<style>');
  assert.ok(grammar.patterns.some((p) => p.include === 'source.kscript#tag-shorthand'), '<if $ready> in pages is coloured as an expression');
});

test('kscript.json and standard files are validated with the bundled schemas', () => {
  const root = path.join(__dirname, '../..');
  const pkg = require('../../package.json');
  const ignore = fs.readFileSync(path.join(root, '.vscodeignore'), 'utf8').split(/\r?\n/);
  const urls = pkg.contributes.jsonValidation.map((v) => v.url);
  assert.ok(urls.includes('./schemas/kscript.schema.json'));
  assert.ok(urls.includes('./schemas/kscript-standard.schema.json'));
  for (const v of pkg.contributes.jsonValidation) {
    const schema = JSON.parse(fs.readFileSync(path.join(root, v.url), 'utf8'));
    assert.ok(schema.properties.lint && schema.properties.format, v.url);
    assert.ok(ignore.includes('!' + path.posix.dirname(v.url.replace(/^\.\//, '')) + '/**'), `${v.url} is packaged`);
  }
  const jsonc = pkg.contributes.languages.find((l) => l.id === 'jsonc');
  assert.ok(jsonc.filenames.includes('kscript.json'), 'kscript.json allows comments');
  assert.ok(jsonc.filenames.includes('kscript-standard.json'), 'standard files allow comments');
  assert.ok(jsonc.filenamePatterns.includes('*.kscript-standard.json'), 'named standard files allow comments');
});

test('block dividers go above every block but the first', () => {
  const { dividerLines } = require('../../src/blocks');
  const block = (line) => ({ range: { start: { line, character: 2 }, end: { line: line + 1, character: 0 } } });
  assert.deepStrictEqual(dividerLines([block(1), block(4), block(9)]), [4, 9]);
  assert.deepStrictEqual(dividerLines([]), []);
  assert.deepStrictEqual(dividerLines(undefined), []);
});

test('a kscript older than the extension', () => {
  assert.ok(isOlderThan('0.2.0', '0.3.0'));
  assert.ok(isOlderThan('0.3.0', '1.0.0'));
  assert.ok(isOlderThan('0.3.0', '0.3.1'));
  assert.ok(!isOlderThan('0.3.0', '0.3.0'));
  assert.ok(!isOlderThan('0.4.0', '0.3.9'));
  assert.ok(!isOlderThan('0.0.0-dev', '0.3.0'), 'a source build is never older');
  assert.ok(!isOlderThan('0.2.0', '0.0.0'));
  assert.ok(!isOlderThan(null, '0.3.0'));
  assert.ok(!isOlderThan('kscript', '0.3.0'));
  assert.ok(isOlderThan('v0.2.0', '0.3.0-beta.1'));
});
