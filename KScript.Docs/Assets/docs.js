// KScript docs: theme, search, highlighting, copy buttons and the "try it" editors. No libraries, works offline.
(function () {
  'use strict';

  var root = document.documentElement;
  var THEME_KEY = 'kscript-docs-theme';
  var PLAYGROUND_KEY = 'kscript-docs-playground';

  function esc(s) {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }
  function span(cls, html) { return '<span class="hl-' + cls + '">' + html + '</span>'; }
  function store(key, value) { try { if (value == null) localStorage.removeItem(key); else localStorage.setItem(key, value); } catch (e) { } }
  function load(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }

  // ---------- Theme ----------
  function isDark() {
    var t = root.dataset.theme;
    if (t) return t === 'dark';
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
  }
  var themeButton = document.querySelector('.theme-toggle');
  if (themeButton) themeButton.addEventListener('click', function () {
    var next = isDark() ? 'light' : 'dark';
    root.dataset.theme = next;
    store(THEME_KEY, next);
  });

  // ---------- Example format ----------
  // Examples show in .kscript or XML. The default comes from kscript docs (the project's make.syntax); the reader's
  // pick lasts for this browser tab and switches every example that hasn't been edited.
  var FORMAT_KEY = 'kscript-docs-format';
  var formatListeners = [];
  function preferredFormat() { return root.dataset.format === 'xml' ? 'xml' : 'kscript'; }
  function setPreferredFormat(f) {
    if (f !== 'xml' && f !== 'kscript') return;
    root.dataset.format = f;
    try { sessionStorage.setItem(FORMAT_KEY, f); } catch (e) { }
    formatListeners.forEach(function (listener) { listener(f); });
  }
  document.addEventListener('click', function (e) {
    var b = e.target.closest('.fmt-tabs button');
    if (b) setPreferredFormat(b.dataset.format);
  });

  // ---------- Mobile menu ----------
  var menu = document.querySelector('.menu-toggle');
  if (menu) menu.addEventListener('click', function () {
    var open = document.body.classList.toggle('nav-open');
    menu.setAttribute('aria-expanded', open ? 'true' : 'false');
  });
  document.addEventListener('click', function (e) {
    if (document.body.classList.contains('nav-open') && !e.target.closest('.sidebar') && !e.target.closest('.menu-toggle'))
      document.body.classList.remove('nav-open');
  });

  // ---------- Highlighting ----------
  // Literals that need no quotes: /regex/flags, joan@example.com and 3f2a8c1e-... uuids.
  var LITERALS = "((?<![\\w)\\]$])\\/(?![\\s\\/*])(?:\\\\.|\\[(?:\\\\.|[^\\]\\\\\\n])*\\]|[^\\/\\\\\\n\\[])+\\/[imsx]*(?!\\w))|((?<![\\w.%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+(?![\\w]|-(?!>)))|((?<![\\w-])[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?![\\w]))";
  var TYPES = 'string|int|float|number|bool|list|map|fn|date|any|email|password|handle|uuid|regex';
  var EXPR = new RegExp("('(?:[^'\\\\]|\\\\.)*')|" + LITERALS + "|(\\$[A-Za-z_][\\w]*)|(@[A-Za-z_][\\w.]*)|(->\\s*[A-Za-z_]\\w*)|(\\|>)|\\b(\\d+(?:\\.\\d+)?)\\b|\\b(fn|and|or|not|lt|le|gt|ge|eq|ne|in|step|match|yes|no|true|false|null)\\b", 'g');

  function hlExpr(s) {
    // A new RegExp each time: strings inside call hlTemplate, which calls hlExpr again.
    var re = new RegExp(EXPR.source, 'g'), out = '', last = 0, m;
    while ((m = re.exec(s))) {
      out += esc(s.slice(last, m.index));
      if (m[1]) out += span('str', hlTemplate(m[1]));
      else if (m[2]) out += span('re', esc(m[2]));
      else if (m[3] || m[4]) out += span('str', esc(m[3] || m[4]));
      else if (m[5]) out += span('var', esc(m[5]));
      else if (m[6]) out += span('cmd', esc(m[6]));
      else if (m[7]) out += span('cmd', esc(m[7]));
      else if (m[8]) out += span('kw', esc(m[8]));
      else if (m[9]) out += span('num', m[9]);
      else out += span('kw', m[10]);
      last = m.index + m[0].length;
    }
    return out + esc(s.slice(last));
  }

  // Text with { } expressions and bare $names.
  function hlTemplate(s) {
    var out = '', i = 0;
    while (i < s.length) {
      var open = s.indexOf('{', i);
      if (open < 0) { out += hlVars(s.slice(i)); break; }
      if (s[open + 1] === '{') { out += hlVars(s.slice(i, open + 2)); i = open + 2; continue; }
      var depth = 0, j = open;
      for (; j < s.length; j++) {
        if (s[j] === '{') depth++;
        else if (s[j] === '}' && --depth === 0) break;
      }
      if (j >= s.length) { out += hlVars(s.slice(i)); break; }
      out += hlVars(s.slice(i, open)) + span('br', '{') + hlExpr(s.slice(open + 1, j)) + span('br', '}');
      i = j + 1;
    }
    return out;
  }
  function hlVars(s) { return esc(s).replace(/\$[A-Za-z_][\w]*(?:\.[A-Za-z_]\w*)*/g, function (v) { return span('var', v); }); }

  function hlXml(src) {
    var out = '', i = 0, n = src.length;
    while (i < n) {
      if (src.startsWith('<!--', i)) {
        var e = src.indexOf('-->', i + 4); e = e < 0 ? n : e + 3;
        out += span('com', esc(src.slice(i, e))); i = e; continue;
      }
      if (src[i] === '<' && /[A-Za-z\/?!]/.test(src[i + 1] || '')) {
        var m = /^<[\/?!]?[\w.:-]*/.exec(src.slice(i));
        out += span('tag', esc(m[0])); i += m[0].length;
        // A shorthand start tag holds an expression, not attributes: <if $n > 3>, <match $choice>.
        var sh = /^\s+(?![\w:.-]+\s*=)(?![\/>])\S/.exec(src.slice(i));
        if (sh && /^<(if|elif|while|match|assert)$/.test(m[0])) {
          var close = i;
          // A > ends the tag unless it has a space on both sides with more of the condition after it.
          while ((close = src.indexOf('>', close)) >= 0 && src[close - 1] === ' ' && src[close + 1] === ' ' && /\S/.test(src.slice(close + 1, src.indexOf('\n', close) < 0 ? n : src.indexOf('\n', close)))) close++;
          if (close < 0) close = n;
          out += hlExpr(src.slice(i, close));
          i = close;
          if (i < n) { out += span('tag', '&gt;'); i++; }
          continue;
        }
        while (i < n && src[i] !== '>') {
          var c = src[i];
          if (c === '"' || c === "'") {
            var end = src.indexOf(c, i + 1); end = end < 0 ? n : end + 1;
            out += span('str', esc(c)) + hlExpr(src.slice(i + 1, end - 1)) + span('str', esc(src[end - 1] === c ? c : ''));
            i = end; continue;
          }
          var a = /^[\w:.-]+/.exec(src.slice(i));
          if (a) { out += span('attr', esc(a[0])); i += a[0].length; continue; }
          if (c === '/' || c === '?') { out += span('tag', c); i++; continue; }
          out += esc(c); i++;
        }
        if (i < n) { out += span('tag', '&gt;'); i++; }
        continue;
      }
      var next = src.indexOf('<', i + 1);
      if (next < 0) next = n;
      out += hlTemplate(src.slice(i, next));
      i = next;
    }
    return out;
  }

  // A \ at the end of a line carries a string on to the next, so an escape can be followed by a new line.
  var KS = new RegExp("(\\/\\/[^\\n]*)|(\"(?:[^\"\\\\]|\\\\[\\s\\S])*\")|('(?:[^'\\\\]|\\\\[\\s\\S])*')|" + LITERALS
    + "|(\\$[A-Za-z_]\\w*)|(@[A-Za-z_][\\w.]*)|(->\\s*[A-Za-z_]\\w*)|\\b(\\d+(?:\\.\\d+)?)\\b"
    + "|(:\\s*)(" + TYPES + "|[A-Z]\\w*)(?![\\w-])"
    + "|^(\\s*)(min|max|pattern|one_of|not|validate|normalize|parse)(?=\\s*:\\s*(?![A-Za-z_][\\w.\\[\\]?]*\\s*(?:=|$)))"
    + "|\\b(type|enum)(\\s+)([A-Za-z_]\\w*)(?:(\\s+with\\s*\\[)([^\\]\\n]*)(\\]))?"
    + "|\\b(if|elif|else|for|in|at|while|match|when|fn|return|try|catch|finally|throw|import|from|as|const|echo|break|continue|exit|and|or|not|yes|no|true|false|null|type|enum|with|construct(?=\\())\\b"
    + "|\\b([A-Z][\\w.]*|[a-z_][\\w-]*)(?=\\s*[({])", 'gm');
  // ```lang ... ``` blocks: text up to a closing fence of the same length, with the language or <Type> after the fence.
  var FENCE = /(`{3,})([^\n`]*)\n[\s\S]*?\n[ \t]*\1(?!`)/g;
  function hlKscript(src) {
    var out = '', last = 0, m, fence = new RegExp(FENCE.source, 'g');
    while ((m = fence.exec(src))) {
      out += hlKscriptCode(src.slice(last, m.index));
      var body = m[0].slice(m[1].length + m[2].length);
      var lang = m[2].trim();
      out += span('str', esc(m[1])) + (lang ? span('type', esc(m[2])) : esc(m[2]));
      if (lang === 'html') out += hlXml(body.slice(0, -m[1].length)) + span('str', esc(m[1]));
      else out += span('str', lang ? esc(body) : hlTemplate(body));
      last = m.index + m[0].length;
    }
    return out + hlKscriptCode(src.slice(last));
  }
  function hlKscriptCode(src) {
    var re = new RegExp(KS.source, 'gm'), out = '', last = 0, m;
    while ((m = re.exec(src))) {
      out += esc(src.slice(last, m.index));
      if (m[1]) out += span('com', esc(m[1]));
      else if (m[2]) out += span('str', '"' + hlTemplate(m[2].slice(1, -1)) + '"');
      else if (m[3]) out += span('str', esc(m[3]));
      else if (m[4]) out += span('re', esc(m[4]));
      else if (m[5] || m[6]) out += span('str', esc(m[5] || m[6]));
      else if (m[7]) out += span('var', esc(m[7]));
      else if (m[8] || m[9]) out += span('cmd', esc(m[8] || m[9]));
      else if (m[10]) out += span('num', m[10]);
      else if (m[12]) out += esc(m[11]) + span('type', esc(m[12]));
      else if (m[14]) out += esc(m[13]) + span('kw', esc(m[14]));
      else if (m[15]) {
        // type Admin with [User, Auditable]: the name and the types it is built on.
        out += span('kw', m[15]) + m[16] + span('type', esc(m[17]));
        if (m[18]) out += m[18].replace('with', span('kw', 'with')) + esc(m[19]).replace(/[A-Za-z_]\w*/g, function (t) { return span('type', t); }) + m[20];
      }
      else if (m[21]) out += span('kw', m[21]);
      else out += span('tag', esc(m[22]));
      last = m.index + m[0].length;
    }
    return out + esc(src.slice(last));
  }

  function hlShell(src) {
    return src.split('\n').map(function (line) {
      var hash = line.search(/(^|\s)#/);
      var code = hash < 0 ? line : line.slice(0, hash), comment = hash < 0 ? '' : line.slice(hash);
      var html = esc(code).replace(/^(\s*)(kscript|claude|dotnet|curl|irm|tar|xattr|npm|git)\b( [a-z][a-z-]*(?= |$))?/, function (_, space, cmd, sub) {
          return space + span('cmd', cmd) + (sub ? ' ' + span('kw', sub.slice(1)) : '');
        })
        .replace(/(\s)(--?[\w-]+)/g, '$1' + span('attr', '$2'));
      return html + (comment ? span('com', esc(comment)) : '');
    }).join('\n');
  }

  function hlJson(src) {
    return esc(src).replace(/(\/\/[^\n]*)|("(?:[^"\\]|\\.)*")(\s*:)?|\b(-?\d+(?:\.\d+)?|true|false|null)\b/g, function (all, com, str, colon, lit) {
      if (com) return span('com', com);
      if (str) return colon ? span('attr', str) + colon : span('str', str);
      return span('num', lit);
    });
  }

  // Stylesheets (.ks.css, .ks.scss, .ks.sass): CSS with KScript elements on their own lines and { $x } / { @fn() }
  // expressions. A { is KScript only when $, @ or ! comes next, as in the stylesheet compiler.
  function cssBraceEnd(src, i) {
    var depth = 0, q = null;
    for (var j = i; j < src.length; j++) {
      var c = src[j];
      if (q) { if (c === '\\') j++; else if (c === q) q = null; continue; }
      if (c === '"' || c === "'") q = c;
      else if (c === '{') depth++;
      else if (c === '}' && --depth === 0) return j;
    }
    return -1;
  }
  function cssIsKs(src, i) { return src[i] === '{' && /^\{\s*[$@!]/.test(src.slice(i, i + 40)); }
  // Whether the word at i is part of a selector: the next ; { or } (outside KScript braces) is a {.
  function cssInSelector(src, i) {
    for (var j = i; j < src.length; j++) {
      var c = src[j];
      if (cssIsKs(src, j)) { var e = cssBraceEnd(src, j); if (e < 0) return false; j = e; continue; }
      if (c === '(') { var close = src.indexOf(')', j); if (close < 0) return false; j = close; continue; }
      if (c === ')') return false; // inside ( ), such as a media query
      if (c === '{') return true;
      if (c === ';' || c === '}') return false;
    }
    return false;
  }
  function hlCss(src) {
    var out = '', i = 0, n = src.length, m;
    while (i < n) {
      var c = src[i], rest = src.slice(i);
      var lineStart = i === 0 || /^[ \t]*$/.test(src.slice(src.lastIndexOf('\n', i - 1) + 1, i));
      if (rest.startsWith('/*')) {
        var e = src.indexOf('*/', i + 2); e = e < 0 ? n : e + 2;
        out += span('com', esc(src.slice(i, e))); i = e; continue;
      }
      if (rest.startsWith('//') && lineStart) {
        var nl = src.indexOf('\n', i); nl = nl < 0 ? n : nl;
        out += span('com', esc(src.slice(i, nl))); i = nl; continue;
      }
      if (c === '<' && lineStart && /[A-Za-z\/!]/.test(src[i + 1] || '')) {
        var end = src.indexOf('\n', i); end = end < 0 ? n : end;
        out += hlXml(src.slice(i, end)); i = end; continue;
      }
      if (cssIsKs(src, i)) {
        var close = cssBraceEnd(src, i);
        if (close > 0) { out += span('br', '{') + hlExpr(src.slice(i + 1, close)) + span('br', '}'); i = close + 1; continue; }
      }
      if (rest.startsWith('#{')) {
        var se = src.indexOf('}', i); se = se < 0 ? n : se + 1;
        out += span('var', esc(src.slice(i, se))); i = se; continue;
      }
      if (c === '"' || c === "'") {
        var q = src.indexOf(c, i + 1); q = q < 0 ? n : q + 1;
        out += span('str', esc(src.slice(i, q))); i = q; continue;
      }
      if ((m = /^@[A-Za-z-]+/.exec(rest))) { out += span('kw', esc(m[0])); i += m[0].length; continue; }
      if ((m = /^\$[A-Za-z_][\w-]*/.exec(rest))) { out += span('var', esc(m[0])); i += m[0].length; continue; }
      if ((m = /^#[0-9a-fA-F]{3,8}\b/.exec(rest)) && !cssInSelector(src, i)) { out += span('num', m[0]); i += m[0].length; continue; }
      if (/[\w-]/.test(src[i - 1] || '') === false && (m = /^-?(?:\d*\.)?\d+(?:[a-z]+|%)?/.exec(rest))) {
        out += span('num', esc(m[0])); i += m[0].length; continue;
      }
      if ((m = /^(?:--|-?[A-Za-z_])[\w-]*/.exec(rest)) && /[\w-]/.test(src[i - 1] || '') === false) {
        var after = src.slice(i + m[0].length);
        if (/^\(/.test(after)) out += span('cmd', esc(m[0]));
        else if (/^\s*:(?!:)/.test(after) && !cssInSelector(src, i)) out += span('attr', esc(m[0]));
        else if (cssInSelector(src, i)) out += span('tag', esc(m[0]));
        else out += esc(m[0]);
        i += m[0].length; continue;
      }
      if ((c === '.' || c === '#' || c === ':') && /[A-Za-z_-]/.test(src[i + 1] || '') && cssInSelector(src, i)) {
        m = /^[.#:]+[\w-]+/.exec(rest);
        out += span('tag', esc(m[0])); i += m[0].length; continue;
      }
      out += esc(c); i++;
    }
    return out;
  }

  function highlight(code, lang) {
    if (lang === 'xml' || lang === 'html') return hlXml(code);
    if (lang === 'kscript') return hlKscript(code);
    if (lang === 'css' || lang === 'scss' || lang === 'sass') return hlCss(code);
    if (lang === 'sh' || lang === 'powershell') return hlShell(code);
    if (lang === 'json' || lang === 'jsonc' || lang === 'yaml') return hlJson(code);
    return esc(code);
  }

  function highlightInline(code) {
    if (/^<\/?[A-Za-z][\w.:-]*(?:\s|\/?>)/.test(code) && />$/.test(code)) return hlXml(code);
    if (/^(?:@media\s|:root\s*\{|[\w-]+:\s*\{)/.test(code) && /\{\s*[@$]/.test(code)) return hlCss(code);

    var signature = /^([A-Z][\w.]*)\(([a-z_]\w*(?:\s*:\s*[a-z_]\w*)?(?:\s*,\s*[a-z_]\w*(?:\s*:\s*[a-z_]\w*)?)*)\)$/.exec(code);
    if (signature) {
      return span('tag', esc(signature[1])) + '(' + esc(signature[2]).replace(/(^|,\s*)([a-z_]\w*)/g,
        function (_, before, name) { return before + span('attr', name); }) + ')';
    }

    var listType = /^([A-Za-z_]\w*)((?:\[\])+\??)$/.exec(code);
    if (listType) return span('type', listType[1]) + esc(listType[2]);
    if (/^(?:[\w.]+\s*\(|\$[A-Za-z_]|@[A-Za-z_]|->\s*[A-Za-z_])/.test(code) ||
        (code.startsWith('[') && code.includes('->'))) return hlKscript(code);
    return null;
  }

  document.querySelectorAll('.code').forEach(function (block) {
    var code = block.querySelector('code');
    if (code) code.innerHTML = highlight(code.textContent, block.dataset.lang);
  });

  document.querySelectorAll('.doc :not(pre) > code').forEach(function (code) {
    if (code.childElementCount) return;
    var html = code.classList.contains('sh') ? hlShell(code.textContent) : highlightInline(code.textContent);
    if (html !== null) code.innerHTML = html;
  });

  // ---------- Copy and open in playground ----------
  function copy(text, button) {
    var done = function () { var t = button.textContent; button.textContent = 'Copied'; setTimeout(function () { button.textContent = t; }, 1200); };
    if (navigator.clipboard && window.isSecureContext !== false) navigator.clipboard.writeText(text).then(done, function () { });
    else {
      var area = document.createElement('textarea');
      area.value = text; document.body.appendChild(area); area.select();
      try { document.execCommand('copy'); done(); } catch (e) { }
      area.remove();
    }
  }
  document.addEventListener('click', function (e) {
    var button = e.target.closest('button.copy');
    if (button) {
      var block = button.closest('.code');
      copy(block.querySelector('code').textContent, button);
      return;
    }
    var open = e.target.closest('button.open-playground');
    if (open) {
      var code = open.closest('.code');
      store(PLAYGROUND_KEY, JSON.stringify({ code: code.querySelector('code').textContent + '\n', format: code.dataset.lang }));
      location.href = '/playground';
    }
  });

  // ---------- Try it ----------
  var RUN_ICON = '<svg viewBox="0 0 12 12" aria-hidden="true"><path d="M2 1.5v9l8-4.5z" fill="currentColor"/></svg>';

  function post(path, body) {
    return fetch(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-KScript-Docs': '1' },
      body: JSON.stringify(body)
    }).then(function (r) { return r.json().then(function (j) { j.status = r.status; return j; }); });
  }

  function element(tag, cls, html) {
    var el = document.createElement(tag);
    if (cls) el.className = cls;
    if (html != null) el.innerHTML = html;
    return el;
  }

  function makeTry(box) {
    var isPlayground = box.dataset.title === 'playground';
    var sources = { xml: box.dataset.xml, kscript: box.dataset.kscript };
    var format = sources[preferredFormat()] != null ? preferredFormat() : (box.dataset.format || 'xml');
    if (isPlayground) {
      var saved = load(PLAYGROUND_KEY);
      if (saved) {
        try {
          var s = JSON.parse(saved);
          format = s.format === 'kscript' ? 'kscript' : 'xml';
          sources = {}; sources[format] = s.code;
        } catch (e) { }
      }
    }
    var original = { xml: sources.xml, kscript: sources.kscript };
    box.classList.add('live');

    var bar = element('div', 'try-bar');
    var tabs = element('div', 'tabs');
    tabs.setAttribute('role', 'tablist');
    ['xml', 'kscript'].forEach(function (f) {
      var b = element('button', null, f === 'xml' ? 'XML' : '.kscript');
      b.type = 'button'; b.dataset.format = f; b.setAttribute('role', 'tab');
      b.title = f === 'xml' ? 'Show as XML' : 'Show in the .kscript format';
      tabs.appendChild(b);
    });
    bar.appendChild(tabs);
    bar.appendChild(element('span', 'spacer'));
    var inputButton = element('button', 'link', 'Input'); inputButton.type = 'button'; inputButton.title = 'Text for <input> and other prompts to read';
    var copyButton = element('button', 'link', 'Copy'); copyButton.type = 'button';
    var resetButton = element('button', 'link', 'Reset'); resetButton.type = 'button'; resetButton.title = 'Go back to the example';
    var runButton = element('button', 'run', RUN_ICON + 'Run'); runButton.type = 'button'; runButton.title = 'Run (Ctrl+Enter)';
    [inputButton, copyButton, resetButton, runButton].forEach(function (b) { bar.appendChild(b); });

    var editor = element('div', 'editor');
    var pre = element('pre', 'hl'); pre.setAttribute('aria-hidden', 'true');
    var area = element('textarea', 'code-input');
    area.spellcheck = false; area.setAttribute('wrap', 'off'); area.setAttribute('autocapitalize', 'off'); area.setAttribute('autocomplete', 'off');
    area.setAttribute('aria-label', 'Example code (Ctrl+Enter runs it, Esc leaves the editor)');
    editor.appendChild(pre); editor.appendChild(area);

    var inputPanel = element('div', 'try-input');
    inputPanel.hidden = true;
    var inputLabel = element('label', null, 'Input: each line answers one prompt');
    var inputArea = element('textarea');
    inputArea.spellcheck = false;
    inputPanel.appendChild(inputLabel); inputPanel.appendChild(inputArea);
    if (box.dataset.input) { inputArea.value = box.dataset.input; inputPanel.hidden = false; }

    var output = element('div', 'try-output');
    output.hidden = true;

    box.appendChild(bar); box.appendChild(editor); box.appendChild(inputPanel); box.appendChild(output);

    function render() {
      pre.innerHTML = highlight(area.value, format) + '\n';
      area.style.height = 'auto';
      editor.style.height = '';
      area.style.height = '100%';
    }
    function setCode(code) { area.value = code.replace(/\n$/, ''); render(); }
    function setFormat(f) {
      format = f;
      tabs.querySelectorAll('button').forEach(function (b) { b.setAttribute('aria-selected', b.dataset.format === f ? 'true' : 'false'); });
      if (isPlayground) save();
    }
    function save() { if (isPlayground) store(PLAYGROUND_KEY, JSON.stringify({ code: area.value, format: format })); }
    function showOutput(parts) {
      output.hidden = false;
      output.innerHTML = '';
      output.appendChild(element('div', 'out-bar', parts.bar));
      parts.blocks.forEach(function (b) {
        var p = element('pre', b.cls);
        p.textContent = b.text;
        output.appendChild(p);
      });
    }

    tabs.addEventListener('click', function (e) {
      var b = e.target.closest('button');
      if (!b || b.dataset.format === format) return;
      var to = b.dataset.format;
      var unchanged = sources[format] != null && area.value === sources[format].replace(/\n$/, '');
      if (unchanged && sources[to] != null) { setFormat(to); setCode(sources[to]); setPreferredFormat(to); return; }
      post('/api/convert', { code: area.value, to: to }).then(function (r) {
        if (r.code == null) { showOutput({ bar: '<span class="status-err">Can\'t convert</span>', blocks: [{ cls: 'err', text: r.error || 'This code could not be converted.' }] }); return; }
        sources = {}; sources[to] = r.code;
        setFormat(to); setCode(r.code); save();
        setPreferredFormat(to);
      }, offline);
    });

    function offline() {
      showOutput({ bar: '<span class="status-err">Not connected</span>', blocks: [{ cls: 'err', text: 'Examples run through kscript on your computer. Start the docs with "kscript docs" to run them.' }] });
    }

    function run() {
      runButton.disabled = true;
      runButton.innerHTML = RUN_ICON + 'Running…';
      post('/api/run', { code: area.value, format: format, input: inputArea.value }).then(function (r) {
        var blocks = [];
        if (r.status !== 200) blocks.push({ cls: 'err', text: r.error || ('Error ' + r.status) });
        else {
          if (r.output) blocks.push({ cls: '', text: r.output + (r.truncated ? '\n… (the rest of the output was cut off)' : '') });
          if (r.error) blocks.push({ cls: 'err', text: r.error });
          if (!r.output && !r.error) blocks.push({ cls: 'empty', text: 'The script ran and printed nothing.' });
        }
        var ok = r.status === 200 && !r.error && r.exitCode === 0;
        var label = ok ? '<span class="status-ok">Output</span>' : '<span class="status-err">' + (r.error ? 'Error' : 'Exit code ' + r.exitCode) + '</span>';
        showOutput({ bar: label + (r.ms != null ? '<span>' + r.ms + ' ms</span>' : ''), blocks: blocks });
      }, offline).then(function () {
        runButton.disabled = false;
        runButton.innerHTML = RUN_ICON + 'Run';
      });
    }

    runButton.addEventListener('click', run);
    copyButton.addEventListener('click', function () { copy(area.value, copyButton); });
    resetButton.addEventListener('click', function () {
      var f = original[format] != null ? format : (original.xml != null ? 'xml' : 'kscript');
      if (isPlayground) { store(PLAYGROUND_KEY, null); original = { xml: box.dataset.xml, kscript: box.dataset.kscript }; f = original[preferredFormat()] != null ? preferredFormat() : (box.dataset.format || 'xml'); }
      sources = { xml: original.xml, kscript: original.kscript };
      setFormat(f); setCode(original[f] || ''); output.hidden = true; save();
    });
    inputButton.addEventListener('click', function () {
      inputPanel.hidden = !inputPanel.hidden;
      if (!inputPanel.hidden) inputArea.focus();
    });

    area.addEventListener('input', function () { render(); save(); });
    area.addEventListener('scroll', function () { pre.scrollLeft = area.scrollLeft; });
    area.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); run(); return; }
      if (e.key === 'Escape') { area.blur(); return; }
      var start = area.selectionStart, end = area.selectionEnd, v = area.value;
      if (e.key === 'Tab' && !e.shiftKey) {
        e.preventDefault();
        area.setRangeText('  ', start, end, 'end');
        render(); save();
      } else if (e.key === 'Enter') {
        var lineStart = v.lastIndexOf('\n', start - 1) + 1;
        var indent = /^[ \t]*/.exec(v.slice(lineStart, start))[0];
        if (/(\{|<[A-Za-z][^>\/]*>)\s*$/.test(v.slice(lineStart, start)) && !/^<\//.test(v.slice(end).trimStart())) indent += '  ';
        e.preventDefault();
        area.setRangeText('\n' + indent, start, end, 'end');
        render(); save();
      }
    });

    // Another example (or the switch at the top) changed the format: follow it, unless this example was edited.
    formatListeners.push(function (f) {
      if (f === format) return;
      var unchanged = sources[format] != null && area.value === sources[format].replace(/\n$/, '');
      if (unchanged && sources[f] != null) { setFormat(f); setCode(sources[f]); }
    });

    setFormat(format);
    var startWith = sources[format] != null ? sources[format] : (sources.xml || sources.kscript || '');
    setCode(startWith);
    if (sources.xml == null || sources.kscript == null) {
      tabs.querySelectorAll('button').forEach(function (b) { if (sources[b.dataset.format] == null && !isPlayground) b.title += ' (converts the current code)'; });
    }
  }

  document.querySelectorAll('.try').forEach(makeTry);

  // ---------- Search ----------
  var input = document.getElementById('search');
  var results = document.getElementById('search-results');
  var index = null, loading = null, active = -1, shown = [];

  function loadIndex() {
    if (index) return Promise.resolve(index);
    if (!loading) loading = fetch('/assets/search.json').then(function (r) { return r.json(); }).then(function (data) {
      // An element has an XML title (<break>) and a .kscript one (break): either matches.
      index = data.map(function (e) {
        var t = e.t.toLowerCase(), c = (e.c || '').toLowerCase();
        return {
          e: e, t: t, c: c,
          name: t.replace(/^.*->/, '').replace(/^[@<$]|[()>]+$/g, '').replace(/^kscript /, ''),
          all: (t + ' ' + c + ' ' + (e.d || '') + ' ' + (e.s || '')).toLowerCase()
        };
      });
      return index;
    });
    return loading;
  }

  var KIND_WEIGHT = { page: 6, project: 5, method: 4, command: 4, element: 4, variable: 3, cli: 3, heading: 0 };

  function search(q) {
    q = q.trim().toLowerCase();
    if (!q) return [];
    var bare = q.replace(/^(->|@|<|\$)/, '').replace(/[()>]+$/, '');
    var words = q.split(/\s+/);
    var scored = [];
    index.forEach(function (x) {
      var s = 0;
      if (x.name === bare || x.t === q || (x.c && x.c === q)) s = 100;
      else if (x.name.indexOf(bare) === 0) s = 80 - Math.min(20, x.name.length - bare.length);
      else if (x.c && x.c.indexOf(q) === 0) s = 75 - Math.min(20, x.c.length - q.length);
      else if (x.t.indexOf(q) >= 0 || (x.c && x.c.indexOf(q) >= 0)) s = 60;
      else if (words.every(function (w) { return x.all.indexOf(w) >= 0; })) s = 30;
      else return;
      if (q.indexOf('->') === 0 && x.e.k !== 'method') s -= 30;
      if (q.indexOf('@') === 0 && x.e.k !== 'command') s -= 30;
      if (q.indexOf('<') === 0 && x.e.k !== 'element') s -= 30;
      if (q.indexOf('$') === 0 && x.e.k !== 'variable') s -= 30;
      s += KIND_WEIGHT[x.e.k] || 0;
      scored.push({ x: x, s: s });
    });
    scored.sort(function (a, b) { return b.s - a.s || a.x.t.length - b.x.t.length; });
    return scored.slice(0, 14).map(function (r) { return r.x.e; });
  }

  function mark(text, q) {
    var h = esc(text);
    var w = q.trim().replace(/^(->|@|<|\$)/, '');
    if (!w) return h;
    var i = text.toLowerCase().indexOf(w.toLowerCase());
    if (i < 0) return h;
    return esc(text.slice(0, i)) + '<mark>' + esc(text.slice(i, i + w.length)) + '</mark>' + esc(text.slice(i + w.length));
  }

  function showResults() {
    var q = input.value;
    if (!q.trim()) { results.hidden = true; return; }
    loadIndex().then(function () {
      shown = search(q);
      active = shown.length ? 0 : -1;
      if (!shown.length) {
        results.innerHTML = '<div class="results-empty">Nothing matches “' + esc(q) + '”.</div>';
      } else {
        results.innerHTML = shown.map(function (e, i) {
          var code = e.k === 'method' || e.k === 'command' || e.k === 'element' || e.k === 'variable' || e.k === 'cli' || e.k === 'project';
          var ks = preferredFormat() === 'kscript';
          var text = ks && e.c ? e.c : e.t;
          var kind = ks && e.k === 'element' ? 'statement' : e.k === 'project' ? 'yours' : e.k;
          var title = code ? '<code>' + mark(text, q) + '</code>' : mark(text, q);
          return '<a class="result' + (i === active ? ' active' : '') + '" role="option" href="' + esc(e.u) + '">' +
            '<div class="result-title"><span class="result-kind">' + esc(kind) + '</span>' + title + '<span class="result-section">' + esc(e.s || '') + '</span></div>' +
            (e.d ? '<div class="result-text">' + esc(e.d) + '</div>' : '') + '</a>';
        }).join('');
      }
      results.hidden = false;
    });
  }

  function move(by) {
    var items = results.querySelectorAll('.result');
    if (!items.length) return;
    active = (active + by + items.length) % items.length;
    items.forEach(function (el, i) { el.classList.toggle('active', i === active); });
    items[active].scrollIntoView({ block: 'nearest' });
  }

  if (input) {
    input.addEventListener('focus', function () { loadIndex(); if (input.value) showResults(); });
    input.addEventListener('input', showResults);
    input.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown') { e.preventDefault(); move(1); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); move(-1); }
      else if (e.key === 'Enter' && active >= 0 && shown[active]) { e.preventDefault(); location.href = shown[active].u; results.hidden = true; }
      else if (e.key === 'Escape') { results.hidden = true; input.blur(); }
    });
    document.addEventListener('keydown', function (e) {
      var typing = /INPUT|TEXTAREA|SELECT/.test((document.activeElement || {}).tagName || '');
      if ((e.key === '/' && !typing) || (e.key === 'k' && (e.ctrlKey || e.metaKey))) { e.preventDefault(); input.focus(); input.select(); }
    });
    document.addEventListener('click', function (e) { if (!e.target.closest('.search')) results.hidden = true; });
    var q = new URLSearchParams(location.search).get('q');
    if (q) { input.value = q; input.focus(); showResults(); }
  }

  // ---------- On this page ----------
  var tocLinks = Array.prototype.slice.call(document.querySelectorAll('.toc a'));
  if (tocLinks.length && 'IntersectionObserver' in window) {
    var byId = {};
    tocLinks.forEach(function (a) { byId[decodeURIComponent(a.getAttribute('href').slice(1))] = a; });
    var visible = {};
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) { visible[en.target.id] = en.isIntersecting; });
      var first = tocLinks.find(function (a) { return visible[decodeURIComponent(a.getAttribute('href').slice(1))]; });
      if (first) tocLinks.forEach(function (a) { a.classList.toggle('active', a === first); });
    }, { rootMargin: '-60px 0px -60% 0px' });
    Object.keys(byId).forEach(function (id) { var el = document.getElementById(id); if (el) observer.observe(el); });
  }
})();
