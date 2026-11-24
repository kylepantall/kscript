'use strict';

const { resolveServer } = require('./server');

function registerDocsMcp(vscode, context, resolve = resolveServer) {
  if (!vscode.lm?.registerMcpServerDefinitionProvider || typeof vscode.McpStdioServerDefinition !== 'function') return;

  const { workspace } = vscode;
  const changed = new vscode.EventEmitter();
  context.subscriptions.push(
    changed,
    workspace.onDidChangeWorkspaceFolders(() => changed.fire()),
    workspace.onDidGrantWorkspaceTrust(() => changed.fire()),
    workspace.onDidChangeConfiguration(event => {
      if (event.affectsConfiguration('kscript.server.path')) changed.fire();
    }),
    vscode.lm.registerMcpServerDefinitionProvider('kscript.docs', {
      onDidChangeMcpServerDefinitions: changed.event,
      provideMcpServerDefinitions() {
        if (!workspace.isTrusted) return [];

        const folders = workspace.workspaceFolders || [];
        return (folders.length ? folders : [undefined]).flatMap(folder => {
          const path = workspace.getConfiguration('kscript', folder?.uri).get('server.path', '');
          const args = folder ? ['mcp', '--dir', folder.uri.fsPath] : ['mcp'];
          const server = resolve(path, args);
          if (server.error) return [];

          let label = 'KScript Docs';
          if (folders.length > 1) {
            const duplicate = folders.some(other => other !== folder && other.name === folder.name);
            label += ` (${duplicate ? folder.uri.fsPath : folder.name})`;
          }
          return [new vscode.McpStdioServerDefinition(label, server.command, server.args)];
        });
      },
    }),
  );
}

module.exports = { registerDocsMcp };
