// The commands that run kscript from the editor: Run Script, Run Tests, Serve Site, Check Project, Format Project,
// New Project and Open Docs. Each is a VS Code task, so its output is in the terminal and can be run again.
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { RUN_ENV, untitledPath, isTestFile, docsArgs } = require('./run');

const KSCRIPT_LANGUAGES = ['kscript', 'kscript-code'];

/**
 * @param vscode the vscode API
 * @param context the extension context
 * @param server () => the kscript to run: { command, args } or { error }
 */
function registerCommands(vscode, context, server) {
  function kscript(what) {
    const found = server();
    if (found.error) {
      vscode.window.showWarningMessage(`${found.error} Install KScript to ${what}.`);
      return null;
    }
    return found;
  }

  function folderOf(uri) {
    return (uri && uri.scheme === 'file' && vscode.workspace.getWorkspaceFolder(uri)) || (vscode.workspace.workspaceFolders || [])[0];
  }

  /** Starts `kscript <args>` as a task. Resolves with its exit code when it ends (undefined if it was closed). */
  async function runTask(found, name, command, args, options = {}) {
    const folder = options.folder;
    const cwd = options.cwd || (folder && folder.uri.fsPath) || os.homedir();
    // A process execution passes each argument as is, so paths with spaces work in every shell.
    const execution = new vscode.ProcessExecution(found.command, [...found.args, ...args], { cwd, env: options.env });
    const scope = folder || (vscode.workspace.workspaceFolders ? vscode.TaskScope.Workspace : vscode.TaskScope.Global);
    const task = new vscode.Task({ type: 'kscript', command, file: options.file }, scope, name, 'kscript', execution, options.matcher ? [options.matcher] : []);
    if (options.group) task.group = options.group;
    task.isBackground = !!options.background;
    task.presentationOptions = { reveal: vscode.TaskRevealKind.Always, clear: true, focus: !!options.focus, showReuseMessage: false };
    const started = await vscode.tasks.executeTask(task);
    return new Promise((resolve) => {
      const done = vscode.tasks.onDidEndTaskProcess((e) => {
        if (e.execution !== started) return;
        done.dispose();
        resolve(e.exitCode);
      });
    });
  }

  /** The document a command is about: the one a lens or button names, or the active editor's. */
  async function documentFor(target) {
    const uri = typeof target === 'string' ? vscode.Uri.parse(target) : target && target.scheme ? target : undefined;
    if (!uri) return vscode.window.activeTextEditor && vscode.window.activeTextEditor.document;
    return vscode.workspace.textDocuments.find((d) => d.uri.toString() === uri.toString()) || vscode.workspace.openTextDocument(uri);
  }

  /** The file to run for a document, saved first. A new, unsaved script is written to a temporary file. */
  async function fileFor(doc) {
    if (doc.isUntitled) {
      const file = untitledPath(path.basename(doc.uri.path || doc.fileName), doc.languageId);
      await fs.promises.mkdir(path.dirname(file), { recursive: true });
      await fs.promises.writeFile(file, doc.getText());
      return file;
    }
    if (doc.isDirty && !(await doc.save())) return null;
    return doc.uri.fsPath;
  }

  async function run(target) {
    const doc = await documentFor(target);
    if (!doc || !KSCRIPT_LANGUAGES.includes(doc.languageId)) {
      vscode.window.showInformationMessage('Open a KScript script (.kscript or .ks.xml) to run it.');
      return undefined;
    }
    if (!doc.isUntitled && isTestFile(doc.uri.fsPath)) return runTests(doc.uri);
    const found = kscript('run scripts');
    if (!found) return undefined;
    const file = await fileFor(doc);
    if (!file) return undefined;
    const folder = doc.isUntitled ? undefined : folderOf(doc.uri);
    return runTask(found, `run ${path.basename(file)}`, 'run', [file], {
      folder, cwd: folder ? folder.uri.fsPath : path.dirname(file), file, env: RUN_ENV, matcher: '$kscript-run', focus: true,
    });
  }

  /** Runs `kscript test`: every test in the folder, one file, or the tests of a file whose name contains filter. */
  async function runTests(target, filter) {
    const found = kscript('run tests');
    if (!found) return undefined;
    const doc = target ? await documentFor(target) : undefined;
    const file = doc ? await fileFor(doc) : undefined;
    if (doc && !file) return undefined;
    const folder = folderOf(doc && doc.uri);
    const args = ['test', ...(file ? [file] : []), ...(filter ? ['--filter', filter] : [])];
    const name = file ? `test ${path.basename(file)}${filter ? ` (${filter})` : ''}` : 'test';
    return runTask(found, name, 'test', args, { folder, cwd: folder ? folder.uri.fsPath : file && path.dirname(file), file, group: vscode.TaskGroup.Test });
  }

  /** A task in the project's folder, for commands that are about the whole project. */
  function inProject(command, what, args, options = {}) {
    return async () => {
      const editor = vscode.window.activeTextEditor;
      const folder = folderOf(editor && editor.document.uri);
      if (!folder && options.needsFolder !== false) {
        vscode.window.showInformationMessage(`Open your project's folder to ${what}.`);
        return undefined;
      }
      const found = kscript(what);
      if (!found) return undefined;
      await vscode.workspace.saveAll(false);
      return runTask(found, command, command, args, { folder, ...options });
    };
  }

  async function newProject() {
    const found = kscript('start a project');
    if (!found) return;
    const name = await vscode.window.showInputBox({
      title: 'New KScript Project',
      prompt: 'The name of the project. A folder with this name is made for it.',
      value: 'my-app',
      validateInput: (v) => (/^[A-Za-z0-9][\w.-]*$/.test(v) ? null : 'Use letters, digits, dots, - and _.'),
    });
    if (!name) return;
    const first = (vscode.workspace.workspaceFolders || [])[0];
    const parent = await vscode.window.showOpenDialog({
      title: `Where should ${name} go?`, openLabel: 'Make Project Here', canSelectFolders: true, canSelectFiles: false, canSelectMany: false,
      defaultUri: first ? vscode.Uri.file(path.dirname(first.uri.fsPath)) : vscode.Uri.file(os.homedir()),
    });
    if (!parent || parent.length === 0) return;
    const target = path.join(parent[0].fsPath, name);
    if (fs.existsSync(target)) {
      vscode.window.showWarningMessage(`${target} already exists. Pick another name or folder.`);
      return;
    }
    const code = await runTask(found, `make project ${name}`, 'make', ['make', 'project', target, '--no-interaction'], { cwd: parent[0].fsPath });
    if (code !== 0) return;
    const choice = await vscode.window.showInformationMessage(`${name} is ready.`, 'Open Project');
    if (choice === 'Open Project') vscode.commands.executeCommand('vscode.openFolder', vscode.Uri.file(target));
  }

  context.subscriptions.push(
    vscode.commands.registerCommand('kscript.run', run),
    vscode.commands.registerCommand('kscript.runTests', () => runTests()),
    vscode.commands.registerCommand('kscript.runTestsInFile', (target) => {
      const editor = vscode.window.activeTextEditor;
      if (!target && !editor) return vscode.window.showInformationMessage('Open a .test.kscript or .test.ks.xml file to run its tests.');
      return runTests(target || editor.document.uri);
    }),
    vscode.commands.registerCommand('kscript.runTest', (target, filter) => runTests(target, filter)),
    vscode.commands.registerCommand('kscript.serve', inProject('serve', 'serve the site', ['serve'], { background: true })),
    vscode.commands.registerCommand('kscript.check', inProject('check', 'check the project', ['check'], { matcher: '$kscript-check' })),
    vscode.commands.registerCommand('kscript.formatProject', inProject('fmt', 'format the project', ['fmt'])),
    // "Open docs" in a hover passes the name it is about, and the docs open on what they have for it.
    vscode.commands.registerCommand('kscript.openDocs', (word) =>
      inProject('docs', 'open the docs', docsArgs(word), { background: true, needsFolder: false })()),
    // "3 uses" above a function: the list every use is shown in, as Find All References does.
    vscode.commands.registerCommand('kscript.showReferences', async (target, position) => {
      if (!target || !position) return;
      const uri = vscode.Uri.parse(target);
      const at = new vscode.Position(position.line, position.character);
      const found = await vscode.commands.executeCommand('vscode.executeReferenceProvider', uri, at);
      await vscode.commands.executeCommand('editor.action.showReferences', uri, at, found || []);
    }),
    vscode.commands.registerCommand('kscript.newProject', newProject),
  );

  return { run, runTests };
}

module.exports = { registerCommands, KSCRIPT_LANGUAGES };
