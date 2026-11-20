// Enter in a /// doc comment: VS Code's onEnterRules continue the comment on the next line, but can't take the
// /// off a line that is still empty. This decides when Enter should end the comment instead.
'use strict';

const EMPTY_DOC_COMMENT = /^(\s*)\/\/\/\s*$/;

// The indentation to leave on the line when Enter should end an empty /// comment, or null to press Enter as usual.
function endEmptyDocComment(before, after) {
  if (after.trim() !== '') return null;
  const match = EMPTY_DOC_COMMENT.exec(before);
  return match ? match[1] : null;
}

// The kscript.onEnter command (package.json maps Enter to it in .kscript files).
async function commentEnter(vscode, editor) {
  const selection = editor.selection;
  const line = editor.document.lineAt(selection.active.line);
  const at = selection.active.character;
  const indent = selection.isEmpty ? endEmptyDocComment(line.text.slice(0, at), line.text.slice(at)) : null;
  if (indent === null) {
    await vscode.commands.executeCommand('default:type', { text: '\n' });
    return;
  }
  await editor.edit((edit) => edit.replace(line.range, indent));
}

module.exports = { endEmptyDocComment, commentEnter };
