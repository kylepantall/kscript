'use strict';

const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { watchedPath, watchBinary } = require('../../src/watch');

const fast = { interval: 20, settle: 30 };
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

function tempBinary(content = 'old') {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'kscript-watch-'));
  const file = path.join(dir, 'kscript');
  fs.writeFileSync(file, content);
  fs.utimesSync(file, 1000, 1000);
  return { dir, file };
}

test('watches the executable, or the dll of a source build', () => {
  assert.strictEqual(watchedPath({ command: '/home/k/.kscript/bin/kscript', args: ['lsp'] }), '/home/k/.kscript/bin/kscript');
  assert.strictEqual(watchedPath({ command: 'dotnet', args: ['/src/KScript.dll', 'lsp'] }), '/src/KScript.dll');
});

test('fires once when kscript update renames a new binary into place', async () => {
  const { dir, file } = tempBinary();
  let calls = 0;
  const watcher = watchBinary(file, () => calls++, fast);
  try {
    await wait(60);
    const staged = path.join(dir, '.kscript-update');
    fs.writeFileSync(staged, 'new binary');
    fs.renameSync(staged, file);
    await wait(250);
    assert.strictEqual(calls, 1);
  } finally {
    watcher.dispose();
    fs.rmSync(dir, { recursive: true });
  }
});

test('waits for a missing binary to come back, and stops when disposed', async () => {
  const { dir, file } = tempBinary();
  let calls = 0;
  const watcher = watchBinary(file, () => calls++, fast);
  try {
    await wait(60);
    fs.unlinkSync(file);
    await wait(150);
    assert.strictEqual(calls, 0, 'a missing file is mid-swap, not a change');
    fs.writeFileSync(file, 'new');
    await wait(250);
    assert.strictEqual(calls, 1);

    watcher.dispose();
    fs.writeFileSync(file, 'newer and longer');
    await wait(150);
    assert.strictEqual(calls, 1);
  } finally {
    watcher.dispose();
    fs.rmSync(dir, { recursive: true });
  }
});

test('ignores polls where nothing changed', () => {
  let listener;
  const fakeFs = { watchFile: (f, o, l) => { listener = l; assert.strictEqual(o.interval, 2000); }, unwatchFile: () => {} };
  let calls = 0;
  const watcher = watchBinary('/k', () => calls++, { fs: fakeFs, settle: 0 });
  const same = { mtimeMs: 5, ino: 1, size: 3 };
  listener(same, { ...same });
  watcher.dispose();
  assert.strictEqual(calls, 0);
});
