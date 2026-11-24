// What the Run and test commands need that isn't the vscode API: preparing a task and reading the JUnit report.
// Kept free of the vscode API so it can be unit tested.
'use strict';

const os = require('os');
const path = require('path');

/** Set on the scripts the editor runs, so an error names its file with the whole path. */
const RUN_ENV = { KSCRIPT_ERROR_PATHS: 'full' };

/** Find the source location in a KScript error on a terminal line. */
function runErrorLink(text) {
  const match = /^(\s*ERROR\s+)((.+?):([1-9]\d*)(?::([1-9]\d*))?):\s/.exec(text);
  if (!match || !path.isAbsolute(match[3])) return undefined;
  return {
    startIndex: match[1].length,
    length: match[2].length,
    file: match[3],
    line: Number(match[4]) - 1,
    column: match[5] ? Number(match[5]) - 1 : 0,
  };
}

/** Where a new, unsaved script is written so it can run: the editor's name for it, with the extension its language needs. */
function untitledPath(name, languageId, tmp = os.tmpdir()) {
  const base = String(name || 'Untitled').replace(/[^\w.-]+/g, '-').replace(/\.(kscript|ks\.xml)$/i, '') || 'Untitled';
  return path.join(tmp, 'kscript-untitled', base + (languageId === 'kscript' ? '.ks.xml' : '.kscript'));
}

/** True for name.test.kscript and name.test.ks.xml: files `kscript test` runs. */
function isTestFile(file) {
  return /\.test\.(kscript|ks\.xml)$/i.test(String(file));
}

function decode(text) {
  return String(text).replace(/&(lt|gt|amp|quot|apos|#x[0-9a-fA-F]+|#\d+);/g, (whole, name) => {
    switch (name) {
      case 'lt': return '<';
      case 'gt': return '>';
      case 'amp': return '&';
      case 'quot': return '"';
      case 'apos': return "'";
      default: return String.fromCodePoint(name[1] === 'x' ? parseInt(name.slice(2), 16) : parseInt(name.slice(1), 10));
    }
  });
}

function attributes(tag) {
  const out = {};
  for (const m of tag.matchAll(/([\w:.-]+)="([^"]*)"/g)) out[m[1]] = decode(m[2]);
  return out;
}

/**
 * The tests in a JUnit report written by `kscript test --junit`:
 *   { file, name, seconds, outcome: 'passed' | 'failed' | 'errored' | 'skipped', message, location, output }
 * name is the groups and the test's name joined with " › ". location is where a failing check is, when the report says.
 */
function parseJUnit(xml) {
  const results = [];
  for (const m of String(xml).matchAll(/<testcase\b([^>]*?)(?:\/>|>([\s\S]*?)<\/testcase>)/g)) {
    const a = attributes(m[1]);
    const body = m[2] || '';
    const problem = /<(failure|error)\b([^>]*?)(?:\/>|>([\s\S]*?)<\/\1>)/.exec(body);
    const out = /<system-out>([\s\S]*?)<\/system-out>/.exec(body);
    const result = {
      file: a.file || a.classname,
      name: a.name,
      seconds: Number(a.time) || 0,
      outcome: problem ? (problem[1] === 'failure' ? 'failed' : 'errored') : /<skipped\b/.test(body) ? 'skipped' : 'passed',
    };
    if (problem) {
      let message = decode(problem[3] || '') || attributes(problem[2]).message || '';
      const at = /\nat (.+?):(\d+)(?::(\d+))?\s*$/.exec(message);
      if (at) {
        result.location = { file: at[1], line: Number(at[2]), column: at[3] ? Number(at[3]) : 1 };
        message = message.slice(0, at.index);
      }
      result.message = message;
    }
    if (out) result.output = decode(out[1]);
    results.push(result);
  }
  return results;
}

function escapeRegExp(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** What --filter gets to run one test or group: its full name, or for a test named for each item, the part before the first { }. */
function filterFor(test) {
  const brace = test.template ? test.fullName.indexOf('{') : -1;
  return brace >= 0 ? test.fullName.slice(0, brace) : test.fullName;
}

/** Matches the names a test runs under: itself, or each filled-in name of a test with { } in its name. */
function namePattern(test) {
  if (!test.template) return new RegExp('^' + escapeRegExp(test.fullName) + '$');
  return new RegExp('^' + test.fullName.split(/\{[^}]*\}/).map(escapeRegExp).join('[\\s\\S]*') + '$');
}

/**
 * One outcome for a test from the report's results: failed or errored if any of its runs was (with that run's
 * message), skipped if all were, passed otherwise. Null when the report has no run of it.
 */
function outcomeOf(test, results) {
  const pattern = namePattern(test);
  const runs = results.filter((r) => pattern.test(r.name));
  if (runs.length === 0) return null;
  const bad = runs.find((r) => r.outcome === 'failed' || r.outcome === 'errored');
  const seconds = runs.reduce((sum, r) => sum + r.seconds, 0);
  if (bad) {
    const message = runs.length > 1 ? `${bad.name}\n${bad.message}` : bad.message;
    return { outcome: bad.outcome, message, location: bad.location, seconds, output: bad.output };
  }
  return { outcome: runs.every((r) => r.outcome === 'skipped') ? 'skipped' : 'passed', seconds };
}

/** The file a report could not load at all (a syntax error, say), as kscript reports it: a "(load)" test. */
function loadError(results) {
  const failed = results.find((r) => r.name === '(load)' && r.outcome !== 'passed');
  return failed ? failed.message : null;
}

/** The arguments of kscript docs: the words to search for, when a hover's "Open docs" link names something. */
function docsArgs(word) {
  const text = typeof word === 'string' ? word.trim() : '';
  // A name is all the link sends; anything that could read as an option is left out.
  return text && /^(->)?[\w.:-]+$/.test(text) && !/^-(?!>)/.test(text) ? ['docs', text] : ['docs'];
}

module.exports = { docsArgs, RUN_ENV, runErrorLink, untitledPath, isTestFile, parseJUnit, filterFor, namePattern, outcomeOf, loadError };
