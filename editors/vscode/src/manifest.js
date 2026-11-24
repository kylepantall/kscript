'use strict';

const { findNodeAtLocation, getLocation, parseTree } = require('jsonc-parser');

function manifestContext(text, offset) {
  const location = getLocation(text, offset);
  const [section, name] = location.path;
  const packages = section === 'packages' && typeof name === 'number' && location.path.length === 2;
  const typeKey = section === 'types' && location.isAtPropertyKey && location.path.length === 2;
  const optionKey = section === 'types' && location.isAtPropertyKey && location.path.length === 3;
  if (!packages && !typeKey && !optionKey) return null;

  const previous = location.previousNode;
  const inName = previous && (previous.type === 'property' || previous.type === 'string')
    && previous.offset <= offset && offset <= previous.offset + previous.length;
  const start = inName ? previous.offset : offset;
  const end = inName ? previous.offset + previous.length : offset;
  const parent = findNodeAtLocation(parseTree(text), optionKey ? ['types', name] : [section]);
  const existing = new Set((parent?.children || []).filter(node => node.offset !== start)
    .map(node => node.type === 'property' ? node.children?.[0]?.value : node.value));
  return { packages, typeKey, optionKey, name, start, end, existing, colon: /^\s*:/.test(text.slice(end)) };
}

function manifestSuggestions(at, metadata) {
  if (at.packages) {
    return Object.entries(metadata.requiredPackages || {}).filter(([label]) => !at.existing.has(label))
      .map(([label, libraries]) => ({ label, detail: 'Required by ' + libraries.join(', '), snippet: JSON.stringify(label), sort: '0' }));
  }
  const choices = at.typeKey ? Object.keys(metadata.types || {}) : metadata.types?.[at.name] || [];
  return choices.filter(label => !at.existing.has(label)).map(label => ({
    label,
    detail: at.typeKey ? 'Built-in type defaults' : `Option for ${at.name}`,
    snippet: JSON.stringify(label) + (at.colon ? '' : at.typeKey ? ': { $0 }' : ': $0'),
    sort: '1',
  }));
}

function registerManifestCompletion(vscode, context, getClient) {
  const selector = ['json', 'jsonc'].map(language => ({ scheme: 'file', language, pattern: '**/kscript.json' }));
  context.subscriptions.push(vscode.languages.registerCompletionItemProvider(selector, {
    async provideCompletionItems(document, position, token) {
      const server = getClient();
      const at = manifestContext(document.getText(), document.offsetAt(position));
      if (!server || !at || token.isCancellationRequested) return [];
      try {
        const metadata = await server.sendRequest('kscript/manifestOptions', { textDocument: { uri: document.uri.toString() } }, token);
        if (!metadata) return [];
        return manifestSuggestions(at, metadata).map(({ label, snippet, detail, sort }) => {
          const item = new vscode.CompletionItem(label, at.packages ? vscode.CompletionItemKind.Value : vscode.CompletionItemKind.Property);
          item.detail = detail;
          item.sortText = sort + label;
          item.range = new vscode.Range(document.positionAt(at.start), document.positionAt(at.end));
          item.insertText = new vscode.SnippetString(snippet);
          return item;
        });
      } catch {
        // Older installed language servers still have the bundled JSON schema.
        return [];
      }
    },
  }, '"', '{', '['));
}

module.exports = { registerManifestCompletion, manifestContext, manifestSuggestions };
