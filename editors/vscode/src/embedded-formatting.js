'use strict';

const { posix } = require('node:path');

const fenceScheme = 'kscript-fence';
const aliases = new Map(Object.entries({
  ps1: 'powershell', kscript: 'kscript-code', js: 'javascript', ts: 'typescript', md: 'markdown', tex: 'latex',
  yml: 'yaml', 'ks.css': 'kscript-css', 'ks.scss': 'kscript-scss',
}));
const suffixes = new Map(Object.entries({
  powershell: 'ps1', 'kscript-code': 'kscript', javascript: 'js', typescript: 'ts', markdown: 'md',
  latex: 'tex', 'kscript-css': 'ks.css', 'kscript-scss': 'ks.scss',
}));
const builtInFormatters = new Map(Object.entries({
  css: 'vscode.css-language-features', scss: 'vscode.css-language-features',
  html: 'vscode.html-language-features',
  json: 'vscode.json-language-features', jsonc: 'vscode.json-language-features',
  javascript: 'vscode.typescript-language-features', typescript: 'vscode.typescript-language-features',
}));

function applyFormatting(document, edits) {
  let text = document.getText();
  const changes = edits.map(edit => ({
    start: document.offsetAt(edit.range.start), end: document.offsetAt(edit.range.end), text: edit.newText,
  }));
  // Offsets refer to the original document, including insertions at a shared offset.
  changes.reverse().sort((a, b) => b.start - a.start || b.end - a.end);
  for (const { start, end, text: replacement } of changes) {
    text = text.slice(0, start) + replacement + text.slice(end);
  }
  return text;
}

function embeddedFormatting(vscode, context, getClient) {
  const contents = new Map();
  let sequence = 0;
  context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider(fenceScheme, {
    provideTextDocumentContent: uri => contents.get(uri.toString()),
  }));

  async function formatFence(source, block, options, eol, languages, token) {
    const language = aliases.get(block.language) ?? block.language;
    if (!block.text || token.isCancellationRequested) return null;
    const extension = suffixes.get(language) ?? language;
    const uri = source.uri.with({
      scheme: fenceScheme,
      path: posix.join(posix.dirname(source.uri.path), `fence-${++sequence}.${extension}`),
      query: '', fragment: '',
    });
    const key = uri.toString();
    contents.set(key, block.text + '\n');
    try {
      let embedded = await vscode.workspace.openTextDocument(uri);
      if (languages.has(language) && embedded.languageId !== language) {
        embedded = await vscode.languages.setTextDocumentLanguage(embedded, language);
      }
      if (embedded.languageId === 'plaintext') return null;
      // Opening the document can start built-in activation without waiting for its provider.
      const formatter = builtInFormatters.get(embedded.languageId);
      if (formatter) await vscode.extensions.getExtension(formatter)?.activate();
      const edits = await vscode.commands.executeCommand('vscode.executeFormatDocumentProvider', uri, options);
      if (!edits?.length) return null;

      const text = applyFormatting(embedded, edits).replace(/\r\n/g, '\n').replace(/\n$/, '');
      if (!text) return '';
      const lines = text.split('\n');
      const closing = '`'.repeat(block.length);
      if (lines.some(line => line.replace(/^[ \t]*/, '').startsWith(closing))) return null;
      return lines.map(line => line ? block.indent + line : '').join(eol) + eol;
    } catch {
      // A formatter can reject virtual documents or fail on this fragment.
      return null;
    } finally {
      contents.delete(key);
    }
  }

  return async function provideDocumentFormattingEdits(source, options, token, next) {
    const client = getClient();
    const original = source.getText();
    if (source.languageId !== 'kscript-code' || source.uri.scheme === fenceScheme || !original.includes('```')
      || !client?.initializeResult?.capabilities.experimental?.kscriptFormatWithFences) {
      return next(source, options, token);
    }

    const result = await client.sendRequest('kscript/formatWithFences', {
      textDocument: { uri: source.uri.toString() }, options,
    }, token);
    if (!result || token.isCancellationRequested || result.version !== source.version) return [];

    let text = result.text;
    if (result.fences.length) {
      const eol = text.includes('\r\n') ? '\r\n' : '\n';
      const languages = new Set(await vscode.languages.getLanguages());
      const edits = await Promise.all(result.fences.map(block => formatFence(source, block, options, eol, languages, token)));
      // The server reports body offsets in the formatted file, in source order.
      for (let i = edits.length - 1; i >= 0; i--) {
        if (edits[i] === null) continue;
        const { start, end } = result.fences[i];
        text = text.slice(0, start) + edits[i] + text.slice(end);
      }
    }

    if (source.isClosed || result.version !== source.version || token.isCancellationRequested || text === original) return [];
    return [vscode.TextEdit.replace(new vscode.Range(source.positionAt(0), source.positionAt(original.length)), text)];
  };
}

module.exports = { fenceScheme, embeddedFormatting };
