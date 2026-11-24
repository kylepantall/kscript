'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { registerCommands } = require('../../src/commands');

// Exercise the same command handlers the palette, toolbar and CodeLens invoke.
function setup(onTask = () => 0) {
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
  let terminalLinks;
  const window = {
    registerTerminalLinkProvider: (provider) => { terminalLinks = provider; },
    showWarningMessage: (message) => messages.push(message),
    showInformationMessage: (message) => messages.push(message),
  };
  const vscode = {
    Position: class { constructor(line, character) { Object.assign(this, { line, character }); } },
    Range: class { constructor(start, end) { Object.assign(this, { start, end }); } },
    workspace, window,
    Uri: { file: uri },
    commands: { registerCommand: (id, handler) => commands.set(id, handler) },
    TaskGroup: { Test: {} }, TaskScope: { Workspace: 1, Global: 2 }, TaskRevealKind: { Always: 1 },
    ProcessExecution: class { constructor(command, args, options) { Object.assign(this, { command, args, options }); } },
    Task: class { constructor(definition, scope, name, source, execution, matchers) { Object.assign(this, { definition, scope, execution, matchers }); } },
    tasks: {
      executeTask: async (task) => { tasks.push(task); return task; },
      onDidEndTaskProcess: (callback) => {
        const execution = tasks.at(-1);
        queueMicrotask(() => callback({ execution, exitCode: onTask(execution) }));
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
  return { folders, commands, document, workspace, window, tasks, messages, terminalLinks };
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

test('Check This File saves its target and passes its path and matcher to the CLI', async () => {
  const { commands, document, window, tasks } = setup();
  const folder = path.resolve('scripts with spaces');
  let saves = 0;
  const target = document(folder, 'draft.kscript', async () => { saves++; return true; });
  window.activeTextEditor = { document: document(folder, 'other.kscript', () => assert.fail('saved the active file')) };
  assert.equal(await commands.get('kscript.checkFile')(target.uri), 0);
  assert.equal(saves, 1);
  assert.deepEqual(tasks[0].execution.args, ['check', target.fileName]);
  assert.equal(tasks[0].execution.options.cwd, folder);
  assert.deepEqual(tasks[0].matchers, ['$kscript-check']);
  target.save = async () => false;
  await commands.get('kscript.checkFile')(target.uri);
  target.uri.scheme = 'untitled';
  await commands.get('kscript.checkFile')(target.uri);
  assert.equal(tasks.length, 1);
});

test('Run uses terminal output with full source paths and no problem matcher', async () => {
  const { commands, document, window, tasks } = setup(() => 3);
  const source = document(path.resolve('scripts with spaces'), 'main.kscript');
  window.activeTextEditor = { document: source };
  assert.equal(await commands.get('kscript.run')(), 3);
  assert.deepEqual(tasks[0].execution.args, [source.fileName]);
  assert.deepEqual(tasks[0].execution.options.env, { KSCRIPT_ERROR_PATHS: 'full' });
  assert.deepEqual(tasks[0].matchers, []);
});

test('a terminal failure links to the reported source file and position, including paths with spaces', async () => {
  const { workspace, window, terminalLinks } = setup();
  const file = path.resolve('folder with spaces', 'another script.kscript');
  const terminalLine = `   ERROR  ${file}:2:3: FileNotFound: no folder`;
  const [link] = terminalLinks.provideTerminalLinks({ line: terminalLine });
  assert.equal(terminalLine.slice(link.startIndex, link.startIndex + link.length), `${file}:2:3`);
  assert.equal(terminalLinks.provideTerminalLinks({ line: `ERROR  ${file}:2: No column` })[0].column, 0);
  assert.deepEqual(terminalLinks.provideTerminalLinks({ line: '   ERROR  Something went wrong' }), []);
  const target = { validatePosition: (position) => position };
  workspace.openTextDocument = async (uri) => {
    assert.equal(uri.fsPath, file);
    return target;
  };
  window.showTextDocument = async (doc, { selection }) => {
    assert.equal(doc, target);
    assert.equal(selection.start.line, 1);
    assert.equal(selection.start.character, 2);
    assert.equal(selection.start, selection.end);
  };
  await terminalLinks.handleTerminalLink(link);
});
