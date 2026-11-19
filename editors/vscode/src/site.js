// Detects folders that are `kscript serve` sites, whose .html and .md pages contain KScript.
'use strict';

const fs = require('fs');
const path = require('path');

const MARKERS = ['_components', '_layouts', '_layout.html', 'kscript.json'];

/** True when the folder (or one level below it) has a site marker such as _components/ or _layout.html. */
async function isSiteFolder(folder, depth = 1) {
  let entries;
  try {
    entries = await fs.promises.readdir(folder, { withFileTypes: true });
  } catch {
    return false;
  }
  if (entries.some((e) => MARKERS.includes(e.name))) return true;
  if (depth <= 0) return false;
  for (const e of entries) {
    if (e.isDirectory() && !e.name.startsWith('.') && e.name !== 'node_modules') {
      if (await isSiteFolder(path.join(folder, e.name), depth - 1)) return true;
    }
  }
  return false;
}

module.exports = { isSiteFolder, MARKERS };
