// Notebook-style divider lines between the top-level blocks of a KScript script. The language server works out the
// blocks (kscript/blocks): a comment starts one, a function or loop is one of its own, and a run of one-line
// elements shares one. This file draws a thin line above each block after the first.
'use strict';

/** The lines that get a divider: the first line of every block except the first. */
function dividerLines(blocks) {
  const lines = [];
  for (let i = 1; i < (blocks || []).length; i++) {
    const line = blocks[i].range.start.line;
    if (line > 0 && !lines.includes(line)) lines.push(line);
  }
  return lines;
}

function registerBlockDividers(vscode, context, getClient) {
  const decoration = vscode.window.createTextEditorDecorationType({
    isWholeLine: true,
    borderWidth: '1px 0 0 0',
    borderStyle: 'solid',
    borderColor: new vscode.ThemeColor('kscript.blockDivider'),
  });
  const timers = new Map();

  const enabled = () => vscode.workspace.getConfiguration('kscript').get('blocks.dividers', true);

  async function refresh(editor) {
    if (!editor || editor.document.languageId !== 'kscript') return;
    const client = getClient();
    if (!enabled() || !client || !client.isRunning()) {
      editor.setDecorations(decoration, []);
      return;
    }
    try {
      const version = editor.document.version;
      const result = await client.sendRequest('kscript/blocks', { textDocument: { uri: editor.document.uri.toString() } });
      if (editor.document.version !== version) return;
      const ranges = dividerLines(result && result.blocks).map((line) => new vscode.Range(line, 0, line, 0));
      editor.setDecorations(decoration, ranges);
    } catch {
      // An older kscript without kscript/blocks: no dividers.
    }
  }

  function schedule(document) {
    for (const editor of vscode.window.visibleTextEditors.filter((e) => e.document === document)) {
      clearTimeout(timers.get(editor));
      timers.set(editor, setTimeout(() => refresh(editor), 250));
    }
  }

  const refreshVisible = () => vscode.window.visibleTextEditors.forEach(refresh);

  context.subscriptions.push(
    decoration,
    vscode.window.onDidChangeVisibleTextEditors(refreshVisible),
    vscode.workspace.onDidChangeTextDocument((e) => schedule(e.document)),
    vscode.workspace.onDidChangeConfiguration((e) => { if (e.affectsConfiguration('kscript.blocks')) refreshVisible(); }),
  );
  return refreshVisible;
}

module.exports = { dividerLines, registerBlockDividers };
