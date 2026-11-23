'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { registerTestExplorer } = require('../../src/testing');

test('text edits invalidate test results in the affected workspace folder', () => {
  const uri = (fsPath) => ({ fsPath, scheme: 'file', toString: () => fsPath });
  const files = ['/one/cart.test.kscript', '/one/orders.test.kscript', '/two/cart.test.kscript'];
  const items = new Map(files.map((file) => [file, { uri: uri(file) }]));
  const invalidated = [];
  let changed;
  const controller = {
    items, createRunProfile() {},
    invalidateTestResults: (targets) => invalidated.push([targets].flat().map((item) => item.uri.fsPath)),
  };
  const watcher = { onDidCreate() {}, onDidDelete() {} };
  const vscode = {
    tests: { createTestController: () => controller },
    TestRunProfileKind: { Run: 1 },
    workspace: {
      getWorkspaceFolder: (file) => ({ uri: uri(file.fsPath.split('/').slice(0, 2).join('/')) }),
      createFileSystemWatcher: () => watcher,
      onDidOpenTextDocument() {},
      onDidChangeTextDocument: (handler) => { changed = handler; },
    },
  };
  const context = { subscriptions: [] };
  registerTestExplorer(vscode, context, () => {}, () => {});
  const edit = (file, contentChanges = [{}]) => changed({
    document: { uri: uri(file), languageId: 'kscript-code' }, contentChanges,
  });
  try {
    edit(files[0], []);
    assert.deepEqual(invalidated, []);
    edit(files[0]);
    edit('/one/library.kscript');
    assert.deepEqual(invalidated, [[files[0]], files.slice(0, 2)]);
  } finally {
    context.subscriptions.at(-1).dispose();
  }
});
