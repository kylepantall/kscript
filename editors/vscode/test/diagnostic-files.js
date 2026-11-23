'use strict';

const assert = require('assert').strict;
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

function catchGuards(source) {
  const [declaration, blank, ...body] = source.trimEnd().split(/\r?\n/);
  assert.equal(blank, '');
  const guarded = body.map((line) => '  ' + line).join('\n');
  return declaration + '\n\ntry {\n' + guarded + '\n} catch Validation as err {\n  $log->error($err.message)\n  exit 3\n}\n';
}

const fixtures = [
  {
    file: 'tools/issue-license.kscript',
    diagnostics: ['if $args', "if @env('KSCRIPT_TEST_ISSUER", "if @env('KSCRIPT_TEST_DATABASE"].map((anchor) =>
      ({ code: 'KS5016', severity: 2, anchor, token: 'if', message: /Validation/ })),
    fix: catchGuards,
  },
  {
    file: 'catch-filter.kscript',
    diagnostics: [{ code: 'KS5016', severity: 2, anchor: 'try {', token: 'try', message: /Network/ }],
    fix: (text) => text.replace(" when $err.message == 'offline'", ''),
  },
  {
    file: 'catch-binding.kscript',
    diagnostics: [{ code: 'KS4001', severity: 1, anchor: '$er.message', token: 'er', message: /\$err/ }],
    fix: (text) => text.replace('$er.message', '$err.message'),
    binding: { use: '$err.message', declaration: 'as err', name: 'err' },
  },
  {
    file: 'refined-parameter.kscript',
    fixedOutput: 'PASSKEY_LOGIN',
    diagnostics: [{ code: 'KS5005', severity: 1, anchor: 'null', message: /key/ }],
    fix: (text) => text.replace('@describe_key(null)', "@describe_key('passkey_login')"),
  },
  {
    file: 'passkey-interpolation.kscript',
    fixedOutput: 'passkey_login',
    diagnostics: [
      { code: 'KS6004', severity: 2, anchor: 'kind: string', token: 'kind', message: /never used/ },
      { code: 'KS5012', severity: 1, anchor: 'passkey_{$kind}', message: /PasskeySessionKey/ },
    ],
    fix: (text) => text.replace('= passkey_{$kind}', '= "passkey_{$kind}"'),
    binding: { use: '$kind}', declaration: 'kind: string', name: 'kind' },
  },
];

async function checkFile(runCli, file, expectedCount) {
  let output;
  let exitCode = 0;
  try {
    output = (await runCli(['check', file, '--warnings-as-errors', '--output', 'json'])).stdout;
  } catch (error) {
    if (error.code !== 1) throw error;
    exitCode = error.code;
    output = error.stdout;
  }
  assert.equal(exitCode, expectedCount ? 1 : 0, file + ' strict checker exit status: ' + output);
  const report = JSON.parse(output);
  assert.equal(report.files, 1);
  assert.equal(report.problems.length, expectedCount, file + ': ' + output);
  return report.problems.map((p) => {
    assert.equal(path.resolve(p.file), file);
    return {
      code: p.code, message: p.message, severity: { error: 1, warning: 2, info: 3, hint: 4 }[p.severity],
      range: { start: { line: p.line - 1, character: p.column - 1 }, end: { line: p.endLine - 1, character: p.endColumn - 1 } },
    };
  });
}

async function exerciseGuards(runCli, file) {
  for (const [args, context, database, message] of [
    [[], 'portal', 'configured', 'Supply one product name'],
    [['Widget'], '', 'configured', 'Run the portal wrapper'],
    [['Widget'], 'portal', '  ', 'Configure the database'],
    [['Widget'], 'portal', 'configured', null],
  ]) {
    const env = { ...process.env, KSCRIPT_TEST_ISSUER_CONTEXT: context, KSCRIPT_TEST_DATABASE_URL: database };
    if (message) {
      await assert.rejects(() => runCli([file, ...args], { env }), (error) => {
        assert.equal(error.code, 3);
        assert.equal(error.stdout, '');
        assert.ok(error.stderr.includes(message), error.stderr);
        return true;
      });
    } else {
      const { stdout, stderr } = await runCli([file, ...args], { env });
      assert.equal(stdout.trimEnd(), 'issued: Widget');
      assert.equal(stderr, '');
    }
  }
}

module.exports = async function diagnosticFiles(connection, runCli, diagnosticsAfter, position) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'kscript-diagnostic-files-'));
  try {
    fs.cpSync(path.join(__dirname, 'fixtures', 'diagnostics'), dir, { recursive: true });
    for (const fixture of fixtures) {
      const file = path.join(dir, fixture.file);
      const uri = pathToFileURL(file).href;
      const text = fs.readFileSync(file, 'utf8');
      const fixed = fixture.fix(text);
      assert.notEqual(fixed, text, fixture.file + ' has the intended edit');
      const cli = await checkFile(runCli, file, fixture.diagnostics.length);
      if (fixture.fixedOutput) {
        await assert.rejects(() => runCli([file]), (error) =>
          error.code === 3 && /Shape:.*PasskeySessionKey/.test(error.stderr), fixture.file + ' rejects the value at runtime');
      }
      const published = await diagnosticsAfter(connection, 'didOpen', {
        textDocument: { uri, languageId: 'kscript-code', version: 1, text },
      });
      const lsp = published.map(({ code, message, range, severity }) => ({ code, message, range, severity }));
      // Check the expected ranges independently of the analyzer the two entry points share.
      for (const { code, severity, anchor, token = anchor, message } of fixture.diagnostics) {
        const offset = anchor.indexOf(token);
        assert.ok(offset >= 0, token);
        const range = { start: position(text, anchor, offset), end: position(text, anchor, offset + token.length) };
        const problem = lsp.find((p) => p.code === code && p.range.start.line === range.start.line);
        assert.ok(problem, fixture.file + ' missing ' + code + ' at ' + JSON.stringify(range) + ': ' + JSON.stringify(lsp));
        assert.deepEqual(problem.range, range, fixture.file);
        assert.equal(problem.severity, severity);
        assert.match(problem.message, message);
      }
      // Delivery order is incidental; compare diagnostics by their source positions.
      const byPosition = (a, b) => a.range.start.line - b.range.start.line || a.range.start.character - b.range.start.character || a.code.localeCompare(b.code);
      assert.deepEqual(lsp.sort(byPosition), cli.sort(byPosition), fixture.file + ' CLI and LSP diagnostics');
      assert.deepEqual(await diagnosticsAfter(connection, 'didChange', {
        textDocument: { uri, version: 2 }, contentChanges: [{ text: fixed }],
      }), [], fixture.file + ' clears on an unsaved edit');
      if (fixture.binding) {
        const { use, declaration, name } = fixture.binding;
        const definitions = await connection.sendRequest('textDocument/definition', {
          textDocument: { uri }, position: position(fixed, use, use.indexOf(name) + 1),
        });
        assert.deepEqual(definitions.map((d) => [d.uri, d.range.start]), [
          [uri, position(fixed, declaration, declaration.indexOf(name))],
        ], fixture.file + ' resolves the repaired reference in the unsaved buffer');
      }
      fs.writeFileSync(file, fixed);
      await checkFile(runCli, file, 0);
      if (fixture.fixedOutput) assert.equal((await runCli([file])).stdout.trimEnd(), fixture.fixedOutput);
      if (fixture.file === 'tools/issue-license.kscript') await exerciseGuards(runCli, file);
      await connection.sendNotification('textDocument/didClose', { textDocument: { uri } });
    }
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
  console.log('CLI and live LSP diagnostic ranges, severities and messages matched for ' + fixtures.length + ' fixtures and their fixes.');
};
