'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { registerCommands } = require('../../src/commands');

// Exercise the same command handlers the palette, toolbar and CodeLens invoke.
function setup() {
  const uri = (file) => ({ scheme: 'file', fsPath: file, toString: () => file });
  const folders = ['one', 'two'].map((name) => ({ uri: uri(path.resolve(name)) }));
  const commands = new Map();
  const tasks = [];
  const messages = [];
  const workspace = {
    workspaceFolders: folders,
    textDocuments: [],
    getWorkspaceFolder: (file) => folders.find((f) => file.fsPath.startsWith(f.uri.fsPath + path.sep)),
  };
  const window = {
    showWarningMessage: (message) => messages.push(message),
    showInformationMessage: (message) => messages.push(message),
  };
  const vscode = {
    workspace, window,
    commands: { registerCommand: (id, handler) => commands.set(id, handler) },
    TaskGroup: { Test: {} }, TaskScope: { Workspace: 1, Global: 2 }, TaskRevealKind: { Always: 1 },
    ProcessExecution: class { constructor(command, args, options) { Object.assign(this, { command, args, options }); } },
    Task: class { constructor(definition, scope, name, source, execution) { Object.assign(this, { definition, scope, execution }); } },
    tasks: {
      executeTask: async (task) => { tasks.push(task); return task; },
      onDidEndTaskProcess: (callback) => {
        queueMicrotask(() => callback({ execution: tasks.at(-1), exitCode: 0 }));
        return { dispose() {} };
      },
    },
  };
  registerCommands(vscode, { subscriptions: [] }, () => ({ command: 'kscript', args: [] }));
  function document(folder, name, save) {
    const fileName = path.join(folder, name);
    const doc = { fileName, uri: uri(fileName), isDirty: !!save, languageId: 'kscript-code', save };
    workspace.textDocuments.push(doc);
    return doc;
  }
  return { folders, commands, document, workspace, window, tasks, messages };
}

test('project commands use the active root and save only its dirty files', async () => {
  for (const command of ['runTests', 'formatProject', 'check', 'serve']) {
    const { folders, commands, document, window, tasks } = setup();
    let saves = 0;
    document(folders[0].uri.fsPath, 'other.kscript', () => assert.fail('saved another folder'));
    window.activeTextEditor = { document: document(folders[1].uri.fsPath, 'main.kscript', async () => { saves++; return true; }) };
    assert.equal(await commands.get('kscript.' + command)(), 0);
    assert.equal(saves, 1);
    assert.equal(tasks[0].scope, folders[1]);
    assert.equal(tasks[0].execution.options.cwd, folders[1].uri.fsPath);
  }
});

test('unsuccessful save or cancelled folder choice stops the project task', async () => {
  const { folders, commands, document, window, tasks, messages } = setup();
  window.activeTextEditor = { document: document(folders[0].uri.fsPath, 'main.kscript', async () => false) };
  await commands.get('kscript.formatProject')();
  assert.match(messages[0], /main\.kscript/);
  window.activeTextEditor = undefined;
  window.showWorkspaceFolderPick = async () => undefined;
  await commands.get('kscript.runTests')();
  assert.equal(tasks.length, 0);
  window.showWorkspaceFolderPick = async () => folders[1];
  assert.equal(await commands.get('kscript.runTests')(), 0);
  assert.equal(tasks[0].scope, folders[1]);
});

test('project tests require a folder and standalone files run in their own directory', async () => {
  const { commands, document, window, workspace, tasks, messages } = setup();
  const folder = path.resolve('outside the workspace');
  window.activeTextEditor = { document: document(folder, 'hello.kscript') };
  assert.equal(await commands.get('kscript.run')(), 0);
  assert.equal(tasks[0].execution.options.cwd, folder);
  await commands.get('kscript.runTestsInFile')();
  assert.equal(tasks.length, 1);
  assert.match(messages.pop(), /test\.kscript/);
  workspace.workspaceFolders = undefined;
  await commands.get('kscript.runTests')();
  assert.equal(tasks.length, 1);
  assert.match(messages.pop(), /Open your project's folder/);
  assert.equal(await commands.get('kscript.openDocs')('echo'), 0);
  assert.deepEqual(tasks[1].execution.args, ['docs', 'echo']);
});
