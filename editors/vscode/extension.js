// KScript for VS Code: starts the KScript language server (`kscript lsp`) and connects it to the editor.
'use strict';

const fs = require('fs');
const path = require('path');
const vscode = require('vscode');
const { LanguageClient, TransportKind } = require('vscode-languageclient/node');

let client;

/** Works out how to start the server: a configured path, the bundled server, or `kscript` on the PATH. */
function serverCommand(context) {
  const config = vscode.workspace.getConfiguration('kscript');
  const args = config.get('server.args', ['lsp']);
  let configured = (config.get('server.path') || '').trim();

  if (!configured) {
    const bundled = path.join(context.extensionPath, 'server', 'KScript.dll');
    if (fs.existsSync(bundled)) {
      configured = bundled;
    }
  }

  if (configured.toLowerCase().endsWith('.dll')) {
    return { command: 'dotnet', args: [configured, ...args] };
  }
  return { command: configured || 'kscript', args };
}

async function hasSiteConfig() {
  const found = await vscode.workspace.findFiles('**/kscript.json', '**/node_modules/**', 1);
  return found.length > 0;
}

async function activate(context) {
  const { command, args } = serverCommand(context);
  const serverOptions = {
    run: { command, args, transport: TransportKind.stdio },
    debug: { command, args, transport: TransportKind.stdio },
  };

  const documentSelector = [{ scheme: 'file', language: 'kscript' }, { scheme: 'untitled', language: 'kscript' }];
  if (vscode.workspace.getConfiguration('kscript').get('pages.enabled', true) && (await hasSiteConfig())) {
    // Pages served by `kscript serve`: HTML and Markdown with KScript elements inline.
    documentSelector.push({ scheme: 'file', language: 'html' }, { scheme: 'file', language: 'markdown' });
  }

  const clientOptions = {
    documentSelector,
    synchronize: {
      fileEvents: vscode.workspace.createFileSystemWatcher('**/*.{xml,ks,kscript,html,md}'),
    },
  };

  client = new LanguageClient('kscript', 'KScript', serverOptions, clientOptions);

  context.subscriptions.push(vscode.commands.registerCommand('kscript.restartServer', async () => {
    if (client) {
      await client.restart();
    }
  }));

  try {
    await client.start();
  } catch (err) {
    vscode.window.showErrorMessage(
      `Couldn't start the KScript language server (${command} ${args.join(' ')}). ` +
      'Install KScript and put `kscript` on your PATH, or set "kscript.server.path". ' + (err && err.message ? err.message : ''));
  }
}

function deactivate() {
  return client ? client.stop() : undefined;
}

module.exports = { activate, deactivate, serverCommand };
