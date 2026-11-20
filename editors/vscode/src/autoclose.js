// Closes an XML start tag as it is typed: <echo> becomes <echo>|</echo>. Only for KScript's XML files, and only when the
// '>' ends a real start tag (not a '>' inside an attribute such as test="$a > 1", or -> and => in an expression).
'use strict';

const START_TAG = /^<([A-Za-z_][\w.:-]*)(\s[^<>]*)?>$/;

// The name to close, or null. `before` is the document up to and including the '>' just typed; `after` is the rest.
function closingTagFor(before, after) {
  const open = before.lastIndexOf('<');
  if (open < 0) return null;
  // Attribute values may hold '<' and '>' themselves: drop them before reading the tag.
  const tag = before.slice(open).replace(/"[^"]*"|'[^']*'/g, '""');
  const quoted = before.slice(open).replace(/"[^"]*"|'[^']*'/g, '');
  if (/["']/.test(quoted)) return null;
  const match = START_TAG.exec(tag);
  if (!match || tag.endsWith('/>')) return null;
  const name = match[1];
  if (after.trimStart().startsWith(`</${name}`)) return null;
  return name;
}

// The edit to make after a change, or null: [offset after the '>', name].
function autoCloseChange(document, change) {
  if (change.text !== '>' || change.rangeLength !== 0) return null;
  const end = document.offsetAt(change.range.start) + 1;
  const text = document.getText();
  const name = closingTagFor(text.slice(Math.max(0, end - 2000), end), text.slice(end, end + 200));
  return name ? { offset: end, name } : null;
}

module.exports = { closingTagFor, autoCloseChange };
