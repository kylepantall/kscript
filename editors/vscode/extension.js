// KScript for VS Code: starts the installed KScript language server (`kscript lsp`) and connects it to the editor.
'use strict';

const vscode = require('vscode');
const { LanguageClient, TransportKind, State } = require('vscode-languageclient/node');
const { resolveServer, versionOf, supportsLsp, isOlderThan } = require('./src/server');
const { isSiteFolder } = require('./src/site');
const { registerBlockDividers } = require('./src/blocks');
const { watchedPath, watchBinary } = require('./src/watch');
const { commentEnter } = require('./src/comments');
const { autoCloseChange } = require('./src/autoclose');
const { registerCommands } = require('./src/commands');
const { registerTestExplorer } = require('./src/testing');
const { registerManifestCompletion } = require('./src/manifest');
const { registerDocsMcp } = require('./src/mcp');

const INSTALL_URL = 'https://github.com/kylepantall/kscript#install-no-net-needed';

let client;
let binaryWatcher;
let restarting = false;
let warnedOlder;
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
    vscode.window.showWarningMessage(
      `${server.error} Install it to get KScript errors, completion and hover in the editor.`,
      'Install KScript', 'Set Path…').then((choice) => {
        if (choice === 'Install KScript') vscode.env.openExternal(vscode.Uri.parse(INSTALL_URL));
        if (choice === 'Set Path…') vscode.commands.executeCommand('workbench.action.openSettings', 'kscript.server.path');
      });
    return;
  }

  const version = versionOf(server);
  watchServerBinary(context, server, version);
  output.appendLine(`Using ${server.command}${version ? ` (kscript ${version})` : ''}, found via ${server.source}.`);
  if (version && !supportsLsp(version)) {
    setStatus('$(warning) KScript', `kscript ${version} needs an update. Click for installation help.`, 'kscript.showSetup');
    vscode.window.showWarningMessage(
      `kscript ${version} is too old for the editor features. Run "kscript update" to get the latest release.`, 'Open Terminal').then((choice) => {
        if (choice === 'Open Terminal') runUpdate(server);
      });
    return;
  }

  // An older kscript works, but may lack what this extension expects. Ask once per version, without waiting.
  const extensionVersion = context.extension && context.extension.packageJSON.version;
  if (server.command !== 'dotnet' && isOlderThan(version, extensionVersion) && warnedOlder !== version) {
    warnedOlder = version;
    vscode.window.showWarningMessage(
      `kscript ${version} is older than the KScript extension (${extensionVersion}), so some editor features may be missing or wrong. Update kscript?`,
      'Run kscript update').then((choice) => {
      if (choice === 'Run kscript update') runUpdate(server);
    });
  }

  const serverOptions = {
    run: { command: server.command, args: server.args, transport: TransportKind.stdio },
    debug: { command: server.command, args: server.args, transport: TransportKind.stdio },
  };

  const documentSelector = [
    { scheme: 'file', language: 'kscript' }, { scheme: 'untitled', language: 'kscript' },
    { scheme: 'file', language: 'kscript-code' }, { scheme: 'untitled', language: 'kscript-code' },
  ];
  if (await pagesEnabled()) {
    // Pages served by `kscript serve`: HTML and Markdown with KScript elements inline.
    documentSelector.push({ scheme: 'file', language: 'html' }, { scheme: 'file', language: 'markdown' });
  }

  client = new LanguageClient('kscript', 'KScript', serverOptions, {
    documentSelector,
    outputChannel: output,
    // The server only offers the Run and Run test lenses to an editor that says it can run them.
    // docsCommand lets a hover end with an "Open docs" link, which only this command may run.
    initializationOptions: { codeLens: config().get('codeLens.enabled', true), docsCommand: 'kscript.openDocs' },
    markdown: { isTrusted: { enabledCommands: ['kscript.openDocs'] } },
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

/** Runs `kscript update` in a terminal. The binary watcher restarts the server when it finishes. */
function runUpdate(server) {
  const terminal = vscode.window.createTerminal('kscript update');
  terminal.show();
  terminal.sendText(`"${server.command}" update`);
}

/** Restarts the language server when its kscript is replaced, e.g. by `kscript update`, so it doesn't keep running the old one. */
function watchServerBinary(context, server, version) {
  if (binaryWatcher) binaryWatcher.dispose();
  binaryWatcher = watchBinary(watchedPath(server), async () => {
    if (restarting) return;
    restarting = true;
    try {
      output.appendLine(`${watchedPath(server)} changed; restarting the language server.`);
      await stopClient();
      await startClient(context);
      const now = versionOf(server);
      if (now && now !== version) vscode.window.showInformationMessage(`KScript updated to ${now}. The language server has restarted.`);
    } finally {
      restarting = false;
    }
  });
}

async function stopClient() {
  if (binaryWatcher) {
    binaryWatcher.dispose();
    binaryWatcher = undefined;
  }
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
    vscode.commands.registerTextEditorCommand('kscript.onEnter', (editor) => commentEnter(vscode, editor)),
    vscode.workspace.onDidChangeTextDocument((event) => {
      const editor = vscode.window.activeTextEditor;
      if (!editor || editor.document !== event.document || event.document.languageId !== 'kscript') return;
      if (!config().get('autoCloseTags', true) || event.contentChanges.length !== 1) return;
      const close = autoCloseChange(event.document, event.contentChanges[0]);
      if (close) editor.insertSnippet(new vscode.SnippetString(`$0</${close.name}>`), event.document.positionAt(close.offset), { undoStopBefore: false, undoStopAfter: false });
    }),
    vscode.commands.registerCommand('kscript.showSetup', () => vscode.env.openExternal(vscode.Uri.parse(INSTALL_URL))),
    vscode.workspace.onDidChangeConfiguration(async (e) => {
      if (e.affectsConfiguration('kscript.server') || e.affectsConfiguration('kscript.pages') || e.affectsConfiguration('kscript.codeLens')) {
        await stopClient();
        await startClient(context);
      }
    }),
  );

  const kscript = () => resolveServer(config().get('server.path', ''), []);
  registerCommands(vscode, context, kscript);
  registerTestExplorer(vscode, context, kscript, () => client);
  registerManifestCompletion(vscode, context, () => client?.state === State.Running ? client : null);
  registerDocsMcp(vscode, context);

  await startClient(context);
}

function deactivate() {
  return stopClient();
}

module.exports = { activate, deactivate };
