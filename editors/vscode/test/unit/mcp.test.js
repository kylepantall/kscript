'use strict';

const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const { test } = require('node:test');
const { registerDocsMcp } = require('../../src/mcp');
const manifest = require('../../package.json');

test('KScript docs discovery uses the trusted workspace and the editor CLI setting', () => {
  const events = new EventEmitter();
  const subscribe = event => listener => {
    events.on(event, listener);
    return { dispose: () => events.off(event, listener) };
  };
  const workspace = {
    isTrusted: false,
    workspaceFolders: [{ name: 'notes', uri: { fsPath: '/projects/notes app' } }],
    getConfiguration(section, uri) {
      assert.equal(section, 'kscript');
      return { get(key) {
        assert.equal(key, 'server.path');
        return uri ? '/build/KScript.dll' : '';
      } };
    },
    onDidChangeWorkspaceFolders: subscribe('folders'),
    onDidGrantWorkspaceTrust: subscribe('trust'),
    onDidChangeConfiguration: subscribe('config'),
  };
  const context = { subscriptions: [] };
  let provider;
  let refreshes = 0;
  const vscode = {
    workspace,
    EventEmitter: class {
      event = subscribe('refresh');
      fire() { events.emit('refresh'); }
    },
    McpStdioServerDefinition: class {
      constructor(label, command, args) { Object.assign(this, { label, command, args }); }
    },
    lm: {
      registerMcpServerDefinitionProvider(id, definition) {
        assert.equal(id, manifest.contributes.mcpServerDefinitionProviders[0].id);
        provider = definition;
        provider.onDidChangeMcpServerDefinitions(() => refreshes++);
        return { dispose() {} };
      },
    },
  };
  registerDocsMcp(vscode, context, (path, args) => path
    ? { command: 'dotnet', args: [path, ...args] }
    : { error: 'KScript is missing' });

  assert.deepEqual(provider.provideMcpServerDefinitions(), []);
  workspace.isTrusted = true;
  events.emit('trust');
  const [server] = provider.provideMcpServerDefinitions();
  assert.equal(server.label, 'KScript Docs');
  assert.equal(server.command, 'dotnet');
  assert.deepEqual(server.args, ['/build/KScript.dll', 'mcp', '--dir', '/projects/notes app']);

  workspace.workspaceFolders.push({ name: 'docs', uri: { fsPath: '/projects/docs' } });
  events.emit('folders');
  assert.deepEqual(provider.provideMcpServerDefinitions().map(item => item.label), [
    'KScript Docs (notes)', 'KScript Docs (docs)',
  ]);
  events.emit('config', { affectsConfiguration: name => name === 'kscript.server.path' });
  events.emit('config', { affectsConfiguration: () => false });
  assert.equal(refreshes, 3);
  workspace.workspaceFolders = [];
  assert.deepEqual(provider.provideMcpServerDefinitions(), []);
});

test('editors without the MCP API can activate the extension', () => {
  const context = { subscriptions: [] };
  registerDocsMcp({}, context);
  assert.deepEqual(context.subscriptions, []);
});
