// The Testing view: lists the tests of every *.test.kscript and *.test.ks.xml file (the language server reads them),
// runs them with `kscript test --junit`, and marks each one passed or failed where it is written.
'use strict';

const cp = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { isTestFile, parseJUnit, filterFor, outcomeOf, loadError } = require('./run');

const TEST_FILES = '**/*.test.{kscript,ks.xml}';
const SKIPPED_FOLDERS = '**/{node_modules,.git,_templates}/**';

/**
 * @param vscode the vscode API
 * @param context the extension context
 * @param server () => the kscript to run: { command, args } or { error }
 * @param client () => the running language client, or undefined
 */
function registerTestExplorer(vscode, context, server, client) {
  if (!vscode.tests || !vscode.tests.createTestController) return undefined;
  const controller = vscode.tests.createTestController('kscript', 'KScript');
  /** What each item under a file is: the server's { name, fullName, group, template }. Files have no entry. */
  const tests = new WeakMap();

  function range(r) {
    return new vscode.Range(r.start.line, r.start.character, r.end.line, r.end.character);
  }

  function fileItem(uri) {
    let item = controller.items.get(uri.toString());
    if (!item) {
      item = controller.createTestItem(uri.toString(), vscode.workspace.asRelativePath(uri), uri);
      item.canResolveChildren = true;
      controller.items.add(item);
    }
    return item;
  }

  async function discover() {
    const found = await vscode.workspace.findFiles(TEST_FILES, SKIPPED_FOLDERS);
    const wanted = new Set(found.map((u) => u.toString()));
    controller.items.forEach((item) => {
      if (!wanted.has(item.id)) controller.items.delete(item.id);
    });
    found.forEach(fileItem);
  }

  /** Asks the language server for a file's tests and makes them its children. */
  async function refresh(item) {
    const running = client();
    if (!running) return;
    let reply;
    try {
      reply = await running.sendRequest('kscript/tests', { textDocument: { uri: item.uri.toString() } });
    } catch {
      return; // The server is restarting, or is too old to know the request.
    }
    const build = (list) => list.map((t) => {
      const child = controller.createTestItem(t.fullName, t.name, item.uri);
      child.range = range(t.range);
      tests.set(child, t);
      child.children.replace(build(t.children || []));
      return child;
    });
    item.children.replace(build((reply && reply.tests) || []));
  }

  controller.resolveHandler = async (item) => {
    if (!item) await discover();
    else if (!tests.has(item)) await refresh(item);
  };
  controller.refreshHandler = async () => {
    await discover();
    const files = [];
    controller.items.forEach((item) => files.push(item));
    await Promise.all(files.map(refresh));
  };

  function leaves(item, into = []) {
    if (item.children.size === 0) {
      if (tests.has(item)) into.push(item);
    } else {
      item.children.forEach((child) => leaves(child, into));
    }
    return into;
  }

  function fileOf(item) {
    let top = item;
    while (top.parent) top = top.parent;
    return top;
  }

  function runFile(found, file, filter, token) {
    return new Promise((resolve) => {
      const report = path.join(os.tmpdir(), `kscript-junit-${process.pid}-${Date.now()}-${Math.random().toString(36).slice(2)}.xml`);
      const folder = vscode.workspace.getWorkspaceFolder(file.uri);
      const args = [...found.args, 'test', file.uri.fsPath, '--junit', report, ...(filter ? ['--filter', filter] : [])];
      const child = cp.execFile(found.command, args, { cwd: folder ? folder.uri.fsPath : path.dirname(file.uri.fsPath), maxBuffer: 16 * 1024 * 1024 },
        (error, stdout, stderr) => {
          cancel.dispose();
          let xml = '';
          try {
            xml = fs.readFileSync(report, 'utf8');
            fs.unlinkSync(report);
          } catch {
            // No report: kscript could not start, or was stopped.
          }
          resolve({ results: parseJUnit(xml), output: `${stdout || ''}${stderr || ''}`, failed: !xml && error ? String(error.message || error) : null });
        });
      const cancel = token.onCancellationRequested(() => child.kill());
    });
  }

  async function runHandler(request, token) {
    const run = controller.createTestRun(request);
    const found = server();
    // What to run in each file: null for the whole file, or the one test or group that was asked for.
    const byFile = new Map();
    const include = request.include || (() => {
      const all = [];
      controller.items.forEach((item) => all.push(item));
      return all;
    })();
    for (const item of include) {
      const file = fileOf(item);
      const asked = byFile.get(file);
      if (asked === undefined && item !== file) byFile.set(file, [item]);
      else if (item === file || asked === null) byFile.set(file, null);
      else asked.push(item);
    }
    const excluded = new Set(request.exclude || []);

    for (const [file, asked] of byFile) {
      if (token.isCancellationRequested) break;
      if (file.children.size === 0) await refresh(file);
      const scope = (asked || [file]).flatMap((item) => leaves(item)).filter((item) => !excluded.has(item));
      scope.forEach((item) => run.started(item));
      if (found.error) {
        scope.forEach((item) => run.errored(item, new vscode.TestMessage(`${found.error} Install KScript to run tests.`)));
        continue;
      }
      const only = asked && asked.length === 1 ? filterFor(tests.get(asked[0])) : undefined;
      const { results, output, failed } = await runFile(found, file, only, token);
      if (output) run.appendOutput(output.replace(/\r?\n/g, '\r\n'));
      const broken = failed || loadError(results);
      for (const item of scope) {
        const result = broken ? null : outcomeOf(tests.get(item), results);
        if (broken) {
          run.errored(item, new vscode.TestMessage(broken));
        } else if (!result || result.outcome === 'skipped') {
          run.skipped(item);
        } else if (result.outcome === 'passed') {
          run.passed(item, result.seconds * 1000);
        } else {
          const message = new vscode.TestMessage(result.message || 'The test failed.');
          const at = result.location;
          if (at && path.basename(at.file) === path.basename(file.uri.fsPath)) {
            message.location = new vscode.Location(file.uri, new vscode.Position(at.line - 1, Math.max(0, at.column - 1)));
          } else if (item.range) {
            message.location = new vscode.Location(file.uri, item.range.start);
          }
          if (result.outcome === 'failed') run.failed(item, message, result.seconds * 1000);
          else run.errored(item, message, result.seconds * 1000);
        }
      }
    }
    run.end();
  }

  controller.createRunProfile('Run', vscode.TestRunProfileKind.Run, runHandler, true);

  // Keep the list in step with the files: a test file that is edited, saved, added or removed.
  let timer;
  const changed = (document) => {
    if (document.uri.scheme !== 'file' || !isTestFile(document.uri.fsPath)) return;
    clearTimeout(timer);
    timer = setTimeout(() => refresh(fileItem(document.uri)), 500);
  };
  const watcher = vscode.workspace.createFileSystemWatcher(TEST_FILES);
  context.subscriptions.push(
    controller,
    watcher,
    watcher.onDidCreate((uri) => fileItem(uri)),
    watcher.onDidDelete((uri) => controller.items.delete(uri.toString())),
    vscode.workspace.onDidOpenTextDocument(changed),
    vscode.workspace.onDidChangeTextDocument((e) => changed(e.document)),
    { dispose: () => clearTimeout(timer) },
  );
  return controller;
}

module.exports = { registerTestExplorer, TEST_FILES };
