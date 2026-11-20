// KScript for VS Code: starts the installed KScript language server (`kscript lsp`) and connects it to the editor.
'use strict';

const vscode = require('vscode');
const { LanguageClient, TransportKind, State } = require('vscode-languageclient/node');
const { resolveServer, versionOf, supportsLsp } = require('./src/server');
const { isSiteFolder } = require('./src/site');
const { registerBlockDividers } = require('./src/blocks');

const INSTALL_URL = 'https://github.com/kylepantall/kscript#install-no-net-needed';

let client;
let status;
let output;

function config() {
  return vscode.workspace.getConfiguration('kscript');
}

async function pagesEnabled() {
  const mode = config().get('pages.enabled', 'auto');
  if (mode === true || mode === 'always') return true;
  if (mode === false || mode === 'never') return false;
  // auto: a folder that looks like a `kscript serve` site.
  for (const folder of vscode.workspace.workspaceFolders || []) {
    if (await isSiteFolder(folder.uri.fsPath)) return true;
  }
  const marker = await vscode.workspace.findFiles('{**/_components/*.html,**/_layout.html,**/_layouts/*.html,**/kscript.json}', '**/node_modules/**', 1);
  return marker.length > 0;
}

let refreshBlocks;

async function startClient(context) {
  const server = resolveServer(config().get('server.path', ''), config().get('server.args', ['lsp']));
  if (server.error) {
    setStatus('$(warning) KScript', 'The KScript language server was not found. Click to set it up.', 'kscript.showSetup');
    const choice = await vscode.window.showWarningMessage(
      `${server.error} Install it to get KScript errors, completion and hover in the editor.`,
      'Install KScript', 'Set Path…');
    if (choice === 'Install KScript') vscode.env.openExternal(vscode.Uri.parse(INSTALL_URL));
    if (choice === 'Set Path…') vscode.commands.executeCommand('workbench.action.openSettings', 'kscript.server.path');
    return;
  }

  const version = versionOf(server);
  output.appendLine(`Using ${server.command}${version ? ` (kscript ${version})` : ''}, found via ${server.source}.`);
  if (version && !supportsLsp(version)) {
    const choice = await vscode.window.showWarningMessage(
      `kscript ${version} is too old for the editor features. Run "kscript update" to get the latest release.`, 'Open Terminal');
    if (choice === 'Open Terminal') {
      const terminal = vscode.window.createTerminal('kscript update');
      terminal.show();
      terminal.sendText(`"${server.command}" update`);
    }
    return;
  }

  const serverOptions = {
    run: { command: server.command, args: server.args, transport: TransportKind.stdio },
    debug: { command: server.command, args: server.args, transport: TransportKind.stdio },
  };

  const documentSelector = [{ scheme: 'file', language: 'kscript' }, { scheme: 'untitled', language: 'kscript' }];
  if (await pagesEnabled()) {
    // Pages served by `kscript serve`: HTML and Markdown with KScript elements inline.
    documentSelector.push({ scheme: 'file', language: 'html' }, { scheme: 'file', language: 'markdown' });
  }

  client = new LanguageClient('kscript', 'KScript', serverOptions, {
    documentSelector,
    outputChannel: output,
    synchronize: { fileEvents: vscode.workspace.createFileSystemWatcher('**/*.{xml,ks,kscript,html,md,json}') },
  });
  client.onDidChangeState((e) => {
    if (e.newState === State.Running && refreshBlocks) refreshBlocks();
    if (e.newState === State.Running) setStatus('$(check) KScript', `kscript ${version || ''} language server is running.`, 'kscript.showOutput');
    if (e.newState === State.Stopped) setStatus('$(error) KScript', 'The KScript language server stopped. Click to see its output.', 'kscript.showOutput');
  });

  try {
    await client.start();
  } catch (err) {
    vscode.window.showErrorMessage(`Couldn't start the KScript language server (${server.command} ${server.args.join(' ')}): ${err && err.message ? err.message : err}`);
  }
}

async function stopClient() {
  if (client) {
    const old = client;
    client = undefined;
    try {
      await old.stop();
    } catch {
      // Already stopped.
    }
  }
}

function setStatus(text, tooltip, command) {
  status.text = text;
  status.tooltip = tooltip;
  status.command = command;
  status.show();
}

async function activate(context) {
  output = vscode.window.createOutputChannel('KScript');
  status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 0);
  status.name = 'KScript';
  context.subscriptions.push(output, status);
  refreshBlocks = registerBlockDividers(vscode, context, () => client);

  context.subscriptions.push(
    vscode.commands.registerCommand('kscript.restartServer', async () => {
      await stopClient();
      await startClient(context);
    }),
    vscode.commands.registerCommand('kscript.showOutput', () => output.show()),
    vscode.commands.registerCommand('kscript.showSetup', () => vscode.env.openExternal(vscode.Uri.parse(INSTALL_URL))),
    vscode.commands.registerCommand('kscript.runTests', () => runTests()),
    vscode.commands.registerCommand('kscript.runTestsInFile', (uri) => {
      const file = uri && uri.fsPath ? uri : vscode.window.activeTextEditor && vscode.window.activeTextEditor.document.uri;
      if (!file) return vscode.window.showInformationMessage('Open a .test.ks.xml file to run its tests.');
      return runTests(file);
    }),
    vscode.workspace.onDidChangeConfiguration(async (e) => {
      if (e.affectsConfiguration('kscript.server') || e.affectsConfiguration('kscript.pages')) {
        await stopClient();
        await startClient(context);
      }
    }),
  );

  await startClient(context);
}

/** Runs `kscript test` as a task: the whole workspace folder, or one test file. */
async function runTests(file) {
  const server = resolveServer(config().get('server.path', ''), []);
  if (server.error) {
    vscode.window.showWarningMessage(`${server.error} Install KScript to run tests.`);
    return;
  }
  const folder = (file && vscode.workspace.getWorkspaceFolder(file)) || (vscode.workspace.workspaceFolders || [])[0];
  if (file) {
    const doc = vscode.workspace.textDocuments.find((d) => d.uri.toString() === file.toString());
    if (doc && doc.isDirty) await doc.save();
  }
  // A process execution passes each argument as is, so paths with spaces work in every shell.
  const args = [...server.args, 'test', ...(file ? [file.fsPath] : [])];
  const execution = new vscode.ProcessExecution(server.command, args, folder ? { cwd: folder.uri.fsPath } : {});
  const task = new vscode.Task({ type: 'kscript', command: 'test', file: file ? file.fsPath : undefined },
    folder || vscode.TaskScope.Workspace, file ? `test ${vscode.workspace.asRelativePath(file)}` : 'test', 'kscript', execution);
  task.group = vscode.TaskGroup.Test;
  task.presentationOptions = { reveal: vscode.TaskRevealKind.Always, clear: true };
  await vscode.tasks.executeTask(task);
}

function deactivate() {
  return stopClient();
}

module.exports = { activate, deactivate };
