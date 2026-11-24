const liveNavigation = (() => {
  const entryName = "__kscriptNavigation";
  const pagePolicies = [
    "clear-site-data", "content-security-policy", "content-security-policy-report-only",
    "cross-origin-embedder-policy", "cross-origin-embedder-policy-report-only",
    "cross-origin-opener-policy", "cross-origin-opener-policy-report-only",
    "document-policy", "origin-agent-cluster", "permissions-policy", "referrer-policy", "refresh", "x-frame-options",
  ];
  const scrollPositions = new Map();
  const loadedScripts = new Set();
  let callbacks;
  let current;
  let managedHead;
  let active;
  let engaged = false;
  const epoch = Math.random().toString(36).slice(2);
  let sequence = 0;

  function samePage(left, right) {
    return left.origin === right.origin && left.pathname === right.pathname && left.search === right.search;
  }

  function supported(url) {
    return (url.protocol === "http:" || url.protocol === "https:") && !url.username && !url.password;
  }

  function nextKey() {
    return epoch + ":" + ++sequence;
  }

  function stateFor(entry, previous) {
    return { ...previous, [entryName]: { id: entry.id, view: entry.view } };
  }

  function navigationEntry(state) {
    const entry = state?.[entryName];
    return typeof entry?.id === "string" && typeof entry.view === "string" ? entry : null;
  }

  function saveScroll() {
    if (current) scrollPositions.set(current.id, [window.scrollX, window.scrollY]);
  }

  function fallback(url, replace) {
    if (url.href === location.href) location.reload();
    else if (replace) location.replace(url.href);
    else location.assign(url.href);
    return false;
  }

  function showProgress(transition) {
    document.documentElement.setAttribute("data-ks-navigating", "");
    transition.timer = setTimeout(() => {
      if (active !== transition) return;
      const bar = document.createElement("progress");
      bar.setAttribute("aria-label", "Loading page");
      bar.setAttribute("data-ks-navigation-progress", "");
      Object.assign(bar.style, {
        position: "fixed", inset: "0 0 auto", width: "100%", height: "3px",
        zIndex: "2147483647", pointerEvents: "none", border: "none",
      });
      document.body.append(bar);
      transition.bar = bar;
    }, 150);
  }

  function endProgress(transition) {
    clearTimeout(transition.timer);
    transition.bar?.remove();
    if (active !== transition) return;
    active = undefined;
    document.documentElement.removeAttribute("data-ks-navigating");
  }

  function runtimeScript(script, base) {
    if (!script.hasAttribute("src")) return false;
    const src = new URL(script.getAttribute("src"), base);
    return src.origin === location.origin && (src.pathname === "/_ks/live.js" || src.pathname === "/_kscript/reload.js");
  }

  function typeOf(script) {
    const type = (script.getAttribute("type") || "").trim().toLowerCase();
    if (type === "module") return "module";
    if (!type || /^(?:application|text)\/(?:x-)?(?:ecma|java)script(?:\d\.\d)?$/.test(type)) return "classic";
    return null;
  }

  function headKey(element, base) {
    const attributes = Array.from(element.attributes, attr => {
      const value = attr.name === "href" || attr.name === "src"
        ? new URL(attr.value, base).href : attr.name === "nonce" ? element.nonce : attr.value;
      return [attr.name, value];
    });
    attributes.sort(([a], [b]) => a.localeCompare(b));
    return JSON.stringify([element.localName, attributes, element.textContent]);
  }

  function canRead(response) {
    if (response.headers.has("content-disposition") || pagePolicies.some(name => response.headers.has(name)))
      return false;
    const contentType = response.headers.get("content-type") || "";
    const charset = /;\s*charset\s*=\s*["']?([^;\s"']+)/i.exec(contentType)?.[1];
    return /^text\/html\b/i.test(contentType) && (!charset || /^utf-?8$/i.test(charset));
  }

  function canSwap(doc) {
    if (!doc.body) return false;
    // These instructions apply during a document load and cannot be recreated by swapping nodes.
    const special = "base, meta[http-equiv], script[type=importmap], script[type=speculationrules]";
    return !doc.querySelector(special) && !document.querySelector(special);
  }

  function scripts(root) {
    return Array.from(root.querySelectorAll("script")).filter(typeOf);
  }

  function replaceHead(next, from, to, signal) {
    const existing = new Map();
    for (const element of managedHead) {
      if (element.parentElement !== document.head) continue;
      const key = headKey(element, from);
      if (!existing.has(key)) existing.set(key, []);
      existing.get(key).push(element);
    }
    const nextManaged = new Set();
    const freshScripts = [];
    const styles = [];
    for (const element of next.children) {
      const key = headKey(element, to);
      const reused = existing.get(key)?.shift();
      const node = reused || document.importNode(element, true);
      if (reused) {
        for (const attribute of ["href", "src"]) {
          if (!reused.hasAttribute(attribute)) continue;
          const before = new URL(reused.getAttribute(attribute), from).href;
          if (new URL(reused.getAttribute(attribute), to).href !== before) reused.setAttribute(attribute, before);
        }
      }
      if (!reused && node.localName === "script" && typeOf(node)) freshScripts.push(node);
      if (!reused && node.localName === "link" && node.hasAttribute("href") && node.relList.contains("stylesheet") &&
          !node.relList.contains("alternate") && !node.disabled && (!node.media || matchMedia(node.media).matches)) {
        styles.push(new Promise(resolve => {
          const done = () => {
            node.removeEventListener("load", done);
            node.removeEventListener("error", done);
            signal.removeEventListener("abort", done);
            resolve();
          };
          node.addEventListener("load", done);
          node.addEventListener("error", done);
          signal.addEventListener("abort", done);
        }));
      }
      nextManaged.add(node);
      document.head.append(node);
    }
    for (const element of managedHead) if (!nextManaged.has(element)) element.remove();
    managedHead = nextManaged;
    return { scripts: freshScripts, styles };
  }

  function replaceRootAttributes(next) {
    const root = document.documentElement;
    for (const attr of Array.from(root.attributes)) {
      if (attr.name !== "data-ks-navigating" && !next.hasAttribute(attr.name)) root.removeAttribute(attr.name);
    }
    for (const attr of next.attributes) {
      if (attr.name !== "data-ks-navigating") root.setAttribute(attr.name, attr.value);
    }
  }

  function activateScript(previous, url, signal) {
    if (runtimeScript(previous, url) || (typeOf(previous) === "classic" && previous.noModule && "noModule" in previous))
      return;
    const head = previous.parentElement === document.head;
    const once = head || previous.hasAttribute("live-navigate-once");
    const key = headKey(previous, url);
    if (once && loadedScripts.has(key)) return;

    const script = document.createElement("script");
    for (const attr of previous.attributes)
      script.setAttribute(attr.name, attr.name === "nonce" ? previous.nonce : attr.value);
    if (!script.hasAttribute("async")) script.async = false;
    script.textContent = previous.textContent;
    const needsLoad = script.hasAttribute("src") || typeOf(script) === "module";
    let ready;
    if (needsLoad) {
      ready = new Promise((resolve, reject) => {
        const cleanup = () => signal.removeEventListener("abort", abort);
        const abort = () => {
          cleanup();
          script.remove();
          reject(signal.reason);
        };
        signal.addEventListener("abort", abort, { once: true });
        script.addEventListener("load", () => {
          cleanup();
          if (once) loadedScripts.add(key);
          resolve();
        }, { once: true });
        script.addEventListener("error", () => {
          cleanup();
          // An optional script asset can fail without invalidating an otherwise complete page.
          resolve();
        }, { once: true });
      });
    }
    previous.replaceWith(script);
    if (head) {
      managedHead.delete(previous);
      managedHead.add(script);
    }
    if (!needsLoad && once) loadedScripts.add(key);
    if (ready && script.hasAttribute("async")) {
      ready.catch(() => {});
      return;
    }
    return ready;
  }

  function finishPosition(url, position) {
    const skipFocus = document.activeElement && document.activeElement !== document.body;
    if (!skipFocus) {
      const focus = document.querySelector("[autofocus], main h1, h1") || document.querySelector("main") || document.body;
      if (!focus.hasAttribute("tabindex")) {
        focus.setAttribute("tabindex", "-1");
        focus.addEventListener("blur", () => focus.removeAttribute("tabindex"), { once: true });
      }
      focus.focus({ preventScroll: true });
    }
    if (position) {
      const [left, top] = position;
      window.scrollTo({ left, top, behavior: "instant" });
      return;
    }
    let anchor = null;
    if (url.hash.length > 1) {
      let name = url.hash.slice(1);
      try { name = decodeURIComponent(name); } catch { /* An invalid escape can still be part of an ID. */ }
      anchor = document.getElementById(name) || document.getElementsByName(name)[0];
    }
    if (anchor) anchor.scrollIntoView({ behavior: "instant" });
    else window.scrollTo({ left: 0, top: 0, behavior: "instant" });
  }

  async function visit(destination, options = {}) {
    let url;
    try { url = new URL(destination, document.baseURI); } catch { return false; }
    if (!supported(url)) return false;
    if (url.origin !== location.origin || !callbacks) return fallback(url, options.replace);
    const previousUrl = options.history ? current.url : new URL(location.href);
    const previousDocument = current.documentUrl;
    if (!options.history && samePage(url, previousUrl) && url.hash !== previousUrl.hash)
      return fallback(url, options.replace);

    const detail = { url: new URL(url), previousUrl: new URL(previousUrl), history: !!options.history, replace: !!options.replace };
    if (callbacks.canNavigate?.(detail) === false) return fallback(url, options.history || options.replace);
    if (!document.dispatchEvent(new CustomEvent("kscript:navigating", { detail, cancelable: true }))) {
      if (options.history) fallback(url, true);
      return false;
    }

    const position = options.scroll === false ? [window.scrollX, window.scrollY] :
      options.history ? scrollPositions.get(options.entry.id) : null;
    saveScroll();
    if (active) {
      active.controller.abort();
      endProgress(active);
    }
    const transition = { controller: new AbortController() };
    active = transition;
    showProgress(transition);
    const signal = transition.controller.signal;

    try {
      const response = await fetch(url.href, {
        headers: { Accept: "text/html" },
        credentials: "same-origin",
        mode: "same-origin",
        signal,
      });
      if (!response.ok || !canRead(response)) return fallback(url, options.history || options.replace);
      const finalUrl = new URL(response.url || url.href);
      if (!supported(finalUrl) || finalUrl.origin !== url.origin) return fallback(url, options.history || options.replace);
      finalUrl.hash = url.hash;
      const doc = new DOMParser().parseFromString(await response.text(), "text/html");
      if (active !== transition || signal.aborted) return false;
      if (!canSwap(doc)) return fallback(url, options.history || options.replace);

      detail.url = new URL(finalUrl);
      callbacks.beforeReplace?.(detail);
      if (active !== transition) return false;
      if (!options.history) saveScroll();
      transition.committed = true;
      if (!engaged) {
        current.url = new URL(location.href);
        history.replaceState(stateFor(current, history.state), "", current.url);
        history.scrollRestoration = "manual";
        engaged = true;
      }
      const entry = options.entry
        ? { ...options.entry, url: finalUrl, documentUrl: finalUrl }
        : { id: options.replace ? current.id : nextKey(), view: nextKey(), url: finalUrl, documentUrl: finalUrl };
      if (options.history || options.replace)
        history.replaceState(stateFor(entry, history.state), "", finalUrl);
      else history.pushState(stateFor(entry), "", finalUrl);
      if (!options.history) scrollPositions.delete(entry.id);
      current = entry;

      const head = replaceHead(doc.head, previousDocument, finalUrl, signal);
      const body = document.importNode(doc.body, true);
      const bodyScripts = scripts(body);
      replaceRootAttributes(doc.documentElement);
      document.body.replaceWith(body);
      if (transition.bar) body.append(transition.bar);
      // Restamp relative script and asset URLs by installing the destination URL before adoption.
      await Promise.all(head.styles);
      for (const script of head.scripts.concat(bodyScripts)) {
        if (signal.aborted || active !== transition) return false;
        await activateScript(script, finalUrl, signal);
      }
      if (signal.aborted || active !== transition) return false;
      callbacks.afterReplace?.(finalUrl, detail);
      finishPosition(finalUrl, position);
      endProgress(transition);
      document.dispatchEvent(new CustomEvent("kscript:navigated", { detail }));
      return true;
    } catch {
      if (signal.aborted || active !== transition) return false;
      return fallback(url, options.history || options.replace);
    } finally {
      endProgress(transition);
    }
  }

  function handleClick(event) {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return;
    const link = event.target.closest?.("a[live-navigate][href]");
    if (!(link instanceof HTMLAnchorElement) || link.hasAttribute("download") || link.relList.contains("external") ||
        (link.target && link.target.toLowerCase() !== "_self")) return;
    const url = new URL(link.href, document.baseURI);
    if (!supported(url) || url.origin !== location.origin) return;
    const before = new URL(location.href);
    if (samePage(url, before) && (url.hash || before.hash)) {
      updateCurrentUrl(before);
      if (engaged) saveScroll();
      return;
    }
    event.preventDefault();
    void visit(url);
  }

  function handleHistory(event) {
    const url = new URL(location.href);
    if (!engaged) {
      if (active) {
        active.controller.abort();
        endProgress(active);
      }
      return;
    }
    const entry = navigationEntry(event.state);
    if (samePage(url, current.url) && (!entry || entry.view === current.view)) {
      const restoring = !!entry && entry.id !== current.id;
      current = restoring
        ? { ...entry, url, documentUrl: current.documentUrl }
        : { id: nextKey(), view: current.view, url, documentUrl: current.documentUrl };
      history.replaceState(stateFor(current, history.state), "", url);
      if (restoring && scrollPositions.has(entry.id)) {
        const [left, top] = scrollPositions.get(entry.id);
        window.scrollTo({ left, top, behavior: "instant" });
      }
      return;
    }
    if (!entry) { fallback(url, true); return; }
    void visit(url, { history: true, entry, replace: true });
  }

  function updateCurrentUrl(url) {
    if (current) current.url = new URL(url);
  }

  function install(handlers = {}) {
    if (callbacks) return;
    callbacks = handlers;
    const entry = navigationEntry(history.state);
    const url = new URL(location.href);
    current = { ...(entry || { id: nextKey(), view: nextKey() }), url, documentUrl: url };
    managedHead = new Set(document.head.children);
    for (const script of scripts(document)) {
      if (script.parentElement === document.head || script.hasAttribute("live-navigate-once"))
        loadedScripts.add(headKey(script, current.url));
    }
    if (entry) {
      engaged = true;
      history.scrollRestoration = "manual";
    }
    document.addEventListener("click", handleClick);
    window.addEventListener("popstate", handleHistory);
    window.addEventListener("scroll", () => { if (engaged && !active?.committed) saveScroll(); }, { passive: true });
  }

  return { install, visit, updateCurrentUrl };
})();
