// Watches the kscript binary the language server runs, so the server can restart after `kscript update`.
// Kept free of the vscode API so it can be unit tested.
'use strict';

const fs = require('fs');

/** The file to watch for a resolved server: the executable, or KScript.dll for a source build run with dotnet. */
function watchedPath(server) {
  return server.command === 'dotnet' ? server.args[0] : server.command;
}

/**
 * Calls `onChange` once `file` has been replaced or rewritten and has stayed put for `settle` ms.
 * Polls with fs.watchFile, because `kscript update` swaps the file by renaming, which fs.watch loses track of.
 * A moment where the file is missing (mid-swap) is ignored; the change counts when the new file appears.
 */
function watchBinary(file, onChange, options = {}) {
  const fsImpl = options.fs || fs;
  const interval = options.interval || 2000;
  const settle = options.settle === undefined ? 1000 : options.settle;
  let timer;
  const listener = (curr, prev) => {
    if (curr.mtimeMs === 0) return; // Gone for now.
    if (curr.mtimeMs === prev.mtimeMs && curr.ino === prev.ino && curr.size === prev.size) return;
    clearTimeout(timer);
    timer = setTimeout(onChange, settle);
  };
  fsImpl.watchFile(file, { interval, persistent: false }, listener);
  return {
    dispose() {
      clearTimeout(timer);
      fsImpl.unwatchFile(file, listener);
    },
  };
}

module.exports = { watchedPath, watchBinary };
