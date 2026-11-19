// Finds the kscript executable that runs the language server. Kept free of the vscode API so it can be unit tested.
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const cp = require('child_process');

/**
 * Where the installers put kscript, in the order they are checked after the setting:
 *   install.sh   $KSCRIPT_INSTALL/bin/kscript, default ~/.kscript/bin/kscript
 *   install.ps1  %KSCRIPT_INSTALL%\kscript.exe, default %LOCALAPPDATA%\Programs\kscript\kscript.exe
 */
function installLocations(env = process.env, platform = process.platform, home = os.homedir()) {
  const join = platform === 'win32' ? path.win32.join : path.posix.join;
  const locations = [];
  if (platform === 'win32') {
    if (env.KSCRIPT_INSTALL) locations.push(join(env.KSCRIPT_INSTALL, 'kscript.exe'), join(env.KSCRIPT_INSTALL, 'bin', 'kscript.exe'));
    if (env.LOCALAPPDATA) locations.push(join(env.LOCALAPPDATA, 'Programs', 'kscript', 'kscript.exe'));
    locations.push(join(home, '.kscript', 'bin', 'kscript.exe'));
  } else {
    if (env.KSCRIPT_INSTALL) locations.push(join(env.KSCRIPT_INSTALL, 'bin', 'kscript'));
    locations.push(join(home, '.kscript', 'bin', 'kscript'));
  }
  return locations;
}

/** The first `kscript` on the PATH, or null. */
function findOnPath(env = process.env, platform = process.platform, exists = isFile) {
  const sep = platform === 'win32' ? ';' : ':';
  const join = platform === 'win32' ? path.win32.join : path.posix.join;
  const names = platform === 'win32'
    ? (env.PATHEXT || '.EXE;.CMD;.BAT').split(';').filter(Boolean).map((ext) => 'kscript' + ext.toLowerCase())
    : ['kscript'];
  for (const dir of (env.PATH || env.Path || '').split(sep)) {
    if (!dir) continue;
    for (const name of names) {
      const candidate = join(dir.replace(/^"|"$/g, ''), name);
      if (exists(candidate)) return candidate;
    }
  }
  return null;
}

function isFile(p) {
  try {
    return fs.statSync(p).isFile();
  } catch {
    return false;
  }
}

/** Expands ~ and ${env:NAME} in a configured path. */
function expand(p, env = process.env, home = os.homedir()) {
  return p
    .replace(/^~(?=$|[\\/])/, home)
    .replace(/\$\{env:([A-Za-z_][A-Za-z0-9_]*)\}/g, (_, name) => env[name] || '');
}

/**
 * How to start the server. `configured` is the kscript.server.path setting: a kscript executable, or KScript.dll
 * from a source build (run with dotnet). Returns { command, args, source } or { error } when nothing is found.
 */
function resolveServer(configured, args = ['lsp'], options = {}) {
  const env = options.env || process.env;
  const platform = options.platform || process.platform;
  const home = options.home || os.homedir();
  const exists = options.exists || isFile;

  const setting = (configured || '').trim();
  if (setting) {
    const p = expand(setting, env, home);
    if (p.toLowerCase().endsWith('.dll')) {
      return exists(p)
        ? { command: 'dotnet', args: [p, ...args], source: 'setting' }
        : { error: `kscript.server.path points to ${p}, which doesn't exist.` };
    }
    if (exists(p)) return { command: p, args, source: 'setting' };
    // A bare name such as "kscript-dev" is looked up on the PATH.
    if (!p.includes('/') && !p.includes('\\')) {
      const onPath = findOnPath({ ...env, PATH: env.PATH || env.Path }, platform, (c) => exists(c) && path.basename(c).toLowerCase().startsWith(p.toLowerCase()));
      if (onPath) return { command: onPath, args, source: 'setting' };
    }
    return { error: `kscript.server.path points to ${p}, which doesn't exist.` };
  }

  for (const location of installLocations(env, platform, home)) {
    if (exists(location)) return { command: location, args, source: 'install' };
  }
  const onPath = findOnPath(env, platform, exists);
  if (onPath) return { command: onPath, args, source: 'path' };
  return { error: "KScript isn't installed, or isn't on your PATH." };
}

/** `kscript --version`, or null when the executable doesn't run. */
function versionOf(server, run = cp.execFileSync) {
  try {
    const versionArgs = server.command === 'dotnet' ? [server.args[0], '--version'] : ['--version'];
    return String(run(server.command, versionArgs, { timeout: 10000, encoding: 'utf8', windowsHide: true })).trim().split(/\r?\n/)[0] || null;
  } catch {
    return null;
  }
}

/** True for versions that have `kscript lsp` (0.1.0 and later; source builds report 0.0.0-dev or 1.0.0). */
function supportsLsp(version) {
  if (!version) return false;
  const m = /^(\d+)\.(\d+)\.(\d+)/.exec(version);
  if (!m) return true; // Unknown format: let the server try.
  if (version.startsWith('0.0.0')) return true; // A development build from source.
  return Number(m[1]) > 0 || Number(m[2]) >= 1;
}

module.exports = { installLocations, findOnPath, expand, resolveServer, versionOf, supportsLsp };
