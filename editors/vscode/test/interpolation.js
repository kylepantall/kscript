'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

function tokensOnLine(data, types, lineNumber, source) {
  let line = 0, column = 0;
  const tokens = [];
  for (let i = 0; i < data.length; i += 5) {
    line += data[i];
    column = data[i] ? data[i + 1] : column + data[i + 1];
    if (line !== lineNumber) continue;
    const end = column + data[i + 2];
    assert.ok(!tokens.length || column >= tokens.at(-1).end, 'overlapping semantic tokens on ' + source);
    tokens.push({ column, end, text: source.slice(column, end), type: types[data[i + 3]] });
  }
  return tokens;
}

function writeFixture(root) {
  const file = path.join(root, 'finish.kscript');
  const settings = path.join(root, 'config', 'config.yaml');
  const lines = [
    'fn path(url: string) => $url',
    'fn finish(kind: string) => @path("{$config.passkeys.url}/{$kind}/finish")',
    'fn escaped(kind: string) => ("🙂 {{literal}} { @path($kind) }")',
    "fn literal() => '{$config.passkeys.url}/{$kind}/finish'",
    'echo @finish(\'login\')',
    'echo @escaped(\'login\')',
    'echo @literal()',
  ];
  const text = lines.join('\n');
  fs.mkdirSync(path.dirname(settings));
  fs.writeFileSync(path.join(root, 'kscript.json'), '{"config":{"root":"config"}}');
  fs.writeFileSync(settings, 'passkeys:\n  url: https://example.test\n');
  fs.writeFileSync(file, text);
  return { file, settings, text, lines };
}

module.exports = async function interpolation(connection, capabilities) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'kscript-interpolation-'));
  const { file, settings, text, lines } = writeFixture(root);
  const uri = pathToFileURL(file).href, settingsUri = pathToFileURL(settings).href;
  const request = (method, args = {}) => connection.sendRequest('textDocument/' + method, { textDocument: { uri }, ...args });
  const at = (line, word, offset = 0) => {
    const index = lines[line].indexOf(word);
    assert.notEqual(index, -1, 'missing position for ' + word);
    return { line, character: index + offset };
  };
  const location = (destination, start, length) => ({
    uri: destination, range: { start, end: { line: start.line, character: start.character + length } },
  });
  try {
    await connection.sendNotification('textDocument/didOpen', { textDocument: { uri, languageId: 'kscript-code', version: 1, text } });

    const legend = capabilities.semanticTokensProvider.legend.tokenTypes;
    const { data } = await request('semanticTokens/full');
    const marked = tokensOnLine(data, legend, 1, lines[1]);
    for (const [word, type] of [['$config', 'variable'], ['passkeys', 'property'], ['url', 'property'], ['$kind', 'parameter'], ['/finish', 'string']]) {
      assert.ok(marked.some(t => t.text === word && t.type === type), word + ' should be ' + type + ': ' + JSON.stringify(marked));
    }
    const nested = tokensOnLine(data, legend, 2, lines[2]);
    assert.ok(nested.some(t => t.text === '@path' && t.type === 'function'));
    assert.ok(nested.some(t => t.text === '$kind' && t.type === 'parameter'));
    assert.ok(nested.some(t => t.text.includes('{{literal}}') && t.type === 'string'));
    assert.ok(tokensOnLine(data, legend, 3, lines[3]).some(t => t.type === 'string' && t.text.includes('{$config.passkeys.url}')));

    const expected = location(uri, at(1, 'kind:'), 4);
    for (const cursor of [at(1, '$kind'), at(1, '$kind', 2)]) {
      assert.deepEqual(await request('definition', { position: cursor }), [expected], 'Ctrl-click navigates to the typed parameter');
      const hover = await request('hover', { position: cursor });
      assert.match(hover.contents.value, /kind.*string/);
    }
    for (const [key, start, type] of [['passkeys', { line: 0, character: 0 }, 'map'], ['url', { line: 1, character: 2 }, 'string']]) {
      const position = at(1, '.' + key, 2);
      assert.deepEqual(await request('definition', { position }), [location(settingsUri, start, key.length)], 'Ctrl-click opens the exact settings key');
      const hover = await request('hover', { position });
      assert.match(hover.contents.value, new RegExp(key + '.*' + type));
      assert.doesNotMatch(hover.contents.value, /example\.test/);
    }
    assert.deepEqual(await request('definition', { position: at(2, '@path', 2) }), [location(uri, at(0, 'path'), 4)]);
    assert.deepEqual(await request('definition', { position: at(3, '$kind', 2) }), []);
  } finally {
    await connection.sendNotification('textDocument/didClose', { textDocument: { uri } });
    fs.rmSync(root, { recursive: true, force: true });
  }
};

module.exports.writeFixture = writeFixture;
module.exports.tokensOnLine = tokensOnLine;
