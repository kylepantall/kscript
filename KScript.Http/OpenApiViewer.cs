using System.Net;

namespace KScript.Http
{
    /// <summary>
    /// The /_api page kscript serve answers: the routes' OpenAPI document, read from /_openapi.json, as a page with
    /// every operation, its parameters, body and answers, and a form to try it. Self-contained, so it works offline.
    /// </summary>
    public static class OpenApiViewer
    {
        public static string Page(string title) => Html.Replace("{{title}}", WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "API" : title));

        private const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{title}} · API</title>
<style>
  :root { --bg: #fff; --fg: #1d2330; --muted: #5e6675; --line: #e3e6eb; --card: #f7f8fa; --code: #f1f3f6;
    --get: #1f7a4d; --post: #2457c5; --put: #a35d00; --patch: #7a4bc2; --delete: #b42d2d; --accent: #2457c5; }
  @media (prefers-color-scheme: dark) { :root { --bg: #14161b; --fg: #e6e8ec; --muted: #9aa1ad; --line: #2a2e37; --card: #1b1e25; --code: #222630;
    --get: #4cc38a; --post: #7aa2ff; --put: #f0a646; --patch: #b89bff; --delete: #ff7b7b; --accent: #7aa2ff; } }
  * { box-sizing: border-box; }
  body { margin: 0; font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; background: var(--bg); color: var(--fg); }
  header { padding: 20px 28px; border-bottom: 1px solid var(--line); display: flex; gap: 16px; align-items: baseline; flex-wrap: wrap; }
  header h1 { margin: 0; font-size: 22px; }
  header .meta { color: var(--muted); font-size: 13px; }
  header a { color: var(--accent); font-size: 13px; }
  .layout { display: grid; grid-template-columns: 260px 1fr; min-height: calc(100vh - 70px); }
  nav { border-right: 1px solid var(--line); padding: 16px; position: sticky; top: 0; align-self: start; max-height: 100vh; overflow: auto; }
  nav input { width: 100%; padding: 6px 8px; border: 1px solid var(--line); border-radius: 6px; background: var(--bg); color: var(--fg); margin-bottom: 12px; }
  nav h3 { font-size: 12px; text-transform: uppercase; letter-spacing: .05em; color: var(--muted); margin: 14px 0 6px; }
  nav a { display: flex; gap: 8px; align-items: center; color: var(--fg); text-decoration: none; padding: 3px 4px; border-radius: 4px; font-size: 13px; }
  nav a:hover { background: var(--card); }
  main { padding: 20px 28px 60px; max-width: 980px; }
  .op { border: 1px solid var(--line); border-radius: 10px; margin: 0 0 18px; background: var(--card); }
  .op > summary { list-style: none; cursor: pointer; padding: 12px 16px; display: flex; gap: 12px; align-items: center; }
  .op > summary::-webkit-details-marker { display: none; }
  .op .body { padding: 0 16px 16px; border-top: 1px solid var(--line); }
  .method { font: 600 12px ui-monospace, Menlo, Consolas, monospace; text-transform: uppercase; min-width: 58px; text-align: center; padding: 2px 6px; border-radius: 4px; color: #fff; }
  .m-get { background: var(--get); } .m-post { background: var(--post); } .m-put { background: var(--put); } .m-patch { background: var(--patch); } .m-delete { background: var(--delete); }
  .path { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 14px; }
  .summary { color: var(--muted); font-size: 14px; }
  .deprecated .path { text-decoration: line-through; }
  .lock { margin-left: auto; font-size: 12px; color: var(--muted); }
  h4 { margin: 16px 0 6px; font-size: 13px; text-transform: uppercase; letter-spacing: .04em; color: var(--muted); }
  table { border-collapse: collapse; width: 100%; font-size: 14px; }
  td, th { text-align: left; padding: 6px 8px; border-bottom: 1px solid var(--line); vertical-align: top; }
  code, pre { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 13px; }
  pre { background: var(--code); padding: 10px 12px; border-radius: 6px; overflow: auto; margin: 6px 0; }
  .status { font-weight: 600; font-family: ui-monospace, Menlo, Consolas, monospace; }
  .s2 { color: var(--get); } .s3 { color: var(--muted); } .s4 { color: var(--put); } .s5 { color: var(--delete); }
  .try { margin-top: 12px; display: grid; gap: 8px; }
  .try label { font-size: 13px; color: var(--muted); display: grid; gap: 2px; }
  .try input, .try textarea { font: 13px ui-monospace, Menlo, Consolas, monospace; padding: 6px 8px; border: 1px solid var(--line); border-radius: 6px; background: var(--bg); color: var(--fg); }
  .try textarea { min-height: 110px; }
  button { justify-self: start; padding: 6px 14px; border-radius: 6px; border: 1px solid var(--accent); background: var(--accent); color: #fff; cursor: pointer; font-weight: 600; }
  .muted { color: var(--muted); }
  .tagline { font-size: 18px; margin: 24px 0 10px; }
  @media (max-width: 760px) { .layout { grid-template-columns: 1fr; } nav { position: static; max-height: none; border-right: 0; border-bottom: 1px solid var(--line); } }
</style>
</head>
<body>
<header>
  <h1 id="title">{{title}}</h1>
  <span class="meta" id="meta"></span>
  <a href="/_openapi.json">openapi.json</a>
  <a href="/_openapi.yaml">openapi.yaml</a>
</header>
<div class="layout">
  <nav><input id="filter" placeholder="Filter routes" aria-label="Filter routes"><div id="toc"></div></nav>
  <main id="main"><p class="muted">Loading /_openapi.json…</p></main>
</div>
<script>
const el = (tag, attrs = {}, ...kids) => {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) k === 'class' ? e.className = v : k === 'text' ? e.textContent = v : e.setAttribute(k, v);
  for (const kid of kids) if (kid != null) e.append(kid);
  return e;
};
let doc;
const deref = s => (s && s.$ref) ? doc.components.schemas[s.$ref.split('/').pop()] : s;
const typeName = s => {
  if (!s) return 'any';
  if (s.$ref) return s.$ref.split('/').pop();
  if (s.type === 'array') return typeName(s.items) + '[]';
  if (s.enum) return s.enum.map(v => JSON.stringify(v)).join(' | ');
  return (s.type || 'any') + (s.format ? ' (' + s.format + ')' : '') + (s.pattern ? ' matching ' + s.pattern : '');
};
const example = (s, depth = 0) => {
  s = deref(s) || {};
  if (depth > 4) return null;
  if (s.enum) return s.enum[0];
  switch (s.type) {
    case 'object': { const o = {}; for (const [k, v] of Object.entries(s.properties || {})) if (!v.readOnly) o[k] = example(v, depth + 1); return o; }
    case 'array': return [example(s.items, depth + 1)];
    case 'integer': return 1;
    case 'number': return 1.5;
    case 'boolean': return true;
    case 'string': return s.format === 'email' ? 'someone@example.com' : s.format === 'uuid' ? '00000000-0000-0000-0000-000000000000' : s.format === 'date-time' ? new Date().toISOString() : 'text';
    default: return null;
  }
};
const schemaBlock = s => {
  const d = deref(s);
  if (d && d.type === 'object' && d.properties) {
    const req = new Set(d.required || []);
    const t = el('table', {}, el('tr', {}, el('th', { text: 'Field' }), el('th', { text: 'Type' }), el('th', { text: 'About' })));
    for (const [k, v] of Object.entries(d.properties))
      t.append(el('tr', {}, el('td', {}, el('code', { text: k + (req.has(k) ? '' : '?') })), el('td', {}, el('code', { text: typeName(v) })), el('td', { text: (v.description || '') + (v.readOnly ? ' (read only)' : '') })));
    return el('div', {}, s.$ref ? el('div', { class: 'muted', text: typeName(s) + (d.description ? ': ' + d.description : '') }) : null, t);
  }
  return el('code', { text: typeName(s) });
};
function operation(path, method, op) {
  const sec = op.security && op.security.length;
  const card = el('details', { class: 'op' + (op.deprecated ? ' deprecated' : ''), id: op.operationId },
    el('summary', {}, el('span', { class: 'method m-' + method, text: method }), el('span', { class: 'path', text: path }),
      el('span', { class: 'summary', text: op.summary || '' }), sec ? el('span', { class: 'lock', text: '🔒 bearer token' }) : null));
  const body = el('div', { class: 'body' });
  if (op.description) body.append(el('p', { text: op.description }));
  if (op.deprecated) body.append(el('p', { class: 'muted', text: 'Deprecated.' }));
  const params = op.parameters || [];
  if (params.length) {
    const t = el('table', {}, el('tr', {}, el('th', { text: 'Name' }), el('th', { text: 'In' }), el('th', { text: 'Type' }), el('th', { text: 'About' })));
    for (const p of params) t.append(el('tr', {}, el('td', {}, el('code', { text: p.name + (p.required ? '' : '?') })), el('td', { text: p.in }), el('td', {}, el('code', { text: typeName(p.schema) })), el('td', { text: p.description || '' })));
    body.append(el('h4', { text: 'Parameters' }), t);
  }
  const rb = op.requestBody;
  let bodyType = null, bodySchema = null;
  if (rb) {
    bodyType = Object.keys(rb.content)[0];
    bodySchema = rb.content[bodyType].schema;
    body.append(el('h4', { text: 'Body (' + bodyType + ')' + (rb.required ? '' : ', optional') }), schemaBlock(bodySchema));
  }
  const rt = el('table', {});
  for (const [code, r] of Object.entries(op.responses || {})) {
    const media = r.content ? Object.keys(r.content)[0] : null;
    rt.append(el('tr', {}, el('td', { class: 'status s' + code[0], text: code }), el('td', { text: r.description || '' }),
      el('td', {}, media ? el('code', { text: media + ': ' + typeName(r.content[media].schema) }) : null)));
  }
  body.append(el('h4', { text: 'Answers' }), rt);
  if (op['x-middleware']) body.append(el('h4', { text: 'Middleware' }), el('code', { text: op['x-middleware'].join(', ') }));

  // Try it
  const form = el('div', { class: 'try' });
  const inputs = {};
  for (const p of params) { const i = el('input', { placeholder: typeName(p.schema) }); inputs[p.in + ':' + p.name] = i; form.append(el('label', {}, p.name + ' (' + p.in + ')', i)); }
  let bodyInput = null;
  if (rb && bodyType === 'application/json') { bodyInput = el('textarea', {}); bodyInput.value = JSON.stringify(example(bodySchema), null, 2); form.append(el('label', {}, 'Body', bodyInput)); }
  let token = null;
  if (sec) { token = el('input', { type: 'password', placeholder: 'token' }); token.value = sessionStorage.getItem('ks-token') || ''; form.append(el('label', {}, 'Bearer token', token)); }
  const out = el('pre', { class: 'muted', text: 'Send a request to see the answer.' });
  const send = el('button', { type: 'button', text: 'Send ' + method.toUpperCase() });
  send.onclick = async () => {
    let url = path;
    const q = new URLSearchParams();
    for (const p of params) {
      const v = inputs[p.in + ':' + p.name].value;
      if (p.in === 'path') url = url.replace('{' + p.name + '}', encodeURIComponent(v));
      else if (v !== '') q.append(p.name, v);
    }
    if ([...q].length) url += '?' + q;
    const headers = {};
    if (bodyInput) headers['Content-Type'] = 'application/json';
    if (token) { headers['Authorization'] = 'Bearer ' + token.value; sessionStorage.setItem('ks-token', token.value); }
    out.textContent = method.toUpperCase() + ' ' + url + ' …';
    try {
      const res = await fetch(url, { method: method.toUpperCase(), headers, body: bodyInput ? bodyInput.value : undefined });
      const text = await res.text();
      let shown = text;
      try { shown = JSON.stringify(JSON.parse(text), null, 2); } catch { }
      out.className = '';
      out.textContent = res.status + ' ' + res.statusText + '\n\n' + shown;
    } catch (e) { out.textContent = String(e); }
  };
  form.append(send, out);
  body.append(el('h4', { text: 'Try it' }), form);
  card.append(body);
  return card;
}
async function load() {
  const main = document.getElementById('main');
  try {
    doc = await (await fetch('/_openapi.json', { cache: 'no-store' })).json();
  } catch (e) {
    main.textContent = 'Could not read /_openapi.json: ' + e;
    return;
  }
  document.getElementById('title').textContent = doc.info.title;
  document.title = doc.info.title + ' · API';
  document.getElementById('meta').textContent = 'version ' + doc.info.version + ' · OpenAPI ' + doc.openapi;
  main.textContent = '';
  if (doc.info.description) main.append(el('p', { text: doc.info.description }));
  const toc = document.getElementById('toc');
  const groups = new Map();
  for (const [path, item] of Object.entries(doc.paths))
    for (const [method, op] of Object.entries(item)) {
      const tag = (op.tags && op.tags[0]) || 'routes';
      if (!groups.has(tag)) groups.set(tag, []);
      groups.get(tag).push([path, method, op]);
    }
  if (!groups.size) main.append(el('p', { class: 'muted', text: 'There are no routes to describe yet.' }));
  for (const [tag, ops] of groups) {
    main.append(el('h2', { class: 'tagline', text: tag }));
    toc.append(el('h3', { text: tag }));
    for (const [path, method, op] of ops) {
      main.append(operation(path, method, op));
      const a = el('a', { href: '#' + op.operationId }, el('span', { class: 'method m-' + method, text: method }), el('span', { text: path }));
      a.dataset.search = (method + ' ' + path + ' ' + (op.summary || '')).toLowerCase();
      a.onclick = () => { const d = document.getElementById(op.operationId); if (d) d.open = true; };
      toc.append(a);
    }
  }
  document.getElementById('filter').oninput = e => {
    const q = e.target.value.toLowerCase();
    for (const a of toc.querySelectorAll('a')) a.style.display = a.dataset.search.includes(q) ? '' : 'none';
    for (const d of main.querySelectorAll('details.op')) d.style.display = d.textContent.toLowerCase().includes(q) ? '' : 'none';
  };
  if (location.hash) { const d = document.getElementById(location.hash.slice(1)); if (d) { d.open = true; d.scrollIntoView(); } }
}
load();
</script>
</body>
</html>
""";
    }
}
