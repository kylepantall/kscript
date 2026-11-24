// KScript live runtime. Served by `kscript serve` at /_ks/live.js and added to pages automatically.
// It sends browser events for <live> blocks to the server, which runs the KScript handler and
// returns the block's new HTML. Pages never contain JavaScript of their own.
(function () {
  "use strict";
  var EVENTS = ["click", "input", "change", "submit", "keydown", "keyup", "focusin", "focusout", "cancel", "close",
    "pointerdown", "pointerup", "pointercancel", "dragstart", "dragover", "drop", "dragend",
    "compositionstart", "compositionend"];
  var DRAG_VALUE = "application/x-kscript-drag";
  var timers = Object.create(null);
  var ticks = Object.create(null);
  var restoringFocus = false;
  var lifecycle = new WeakMap();
  // Live proofs identify the URL that rendered the document, including its original query string.
  var renderedUrl = location.pathname + location.search;

  function syncQuery(current, next) {
    if (current === next) return;
    const before = JSON.parse(current || "{}");
    const after = JSON.parse(next || "{}");
    const url = new URL(location.href);
    for (const [name, value] of Object.entries(after)) {
      if (Object.hasOwn(before, name) && before[name] === value) continue;
      if (value === null) url.searchParams.delete(name);
      else url.searchParams.set(name, String(value));
    }
    history.replaceState(history.state, "", url);
  }

  function owned(block, selector) {
    return Array.from(block.querySelectorAll(selector)).filter(el => blockOf(el) === block);
  }

  function setBusy(block, busy) {
    if (busy) block.setAttribute("aria-busy", "true");
    else block.removeAttribute("aria-busy");
    for (const el of owned(block, "[data-ks-live-loading]")) el.hidden = !busy;
    for (const el of owned(block, "[data-ks-live-disable]")) {
      if (busy && !el.disabled && "disabled" in el) {
        el.disabled = true;
        el.setAttribute("data-ks-disabled-for-request", "");
      } else if (!busy && el.hasAttribute("data-ks-disabled-for-request")) {
        el.disabled = false;
        el.removeAttribute("data-ks-disabled-for-request");
      }
    }
  }

  function showError(block, message) {
    for (const el of owned(block, "[data-ks-live-error]")) {
      el.textContent = message || "";
      el.hidden = !message;
    }
  }

  function updateOnline() {
    for (const el of document.querySelectorAll("[data-ks-live-offline]")) el.hidden = navigator.onLine;
  }

  function updateDirty(block, baseline) {
    if (!baseline) return;
    const controls = owned(block, "[data-ks-live-dirty]");
    if (!controls.length) return;
    const changed = new Set(Object.entries(binds(block)).filter(([key, value]) =>
      JSON.stringify(value) !== JSON.stringify(baseline[key])).map(([key]) => key));
    for (const control of controls) {
      const field = control.getAttribute("data-ks-live-dirty");
      control.hidden = field ? !changed.has(field) : changed.size === 0;
    }
  }

  function startLifecycle(block, previous) {
    const state = previous || { initialized: false, visible: false, observer: null };
    if (!previous) state.baseline = binds(block);
    state.observer?.disconnect();
    state.observer = null;
    lifecycle.set(block, state);
    const lazy = block.getAttribute("data-ks-live-lazy");
    if (lazy) {
      const load = () => {
        if (!block.isConnected || state.requested) return;
        state.requested = true;
        state.observer?.disconnect();
        send(block, -2, null, block, { type: "mount" });
      };
      if (lazy === "defer") queueMicrotask(load);
      else {
        state.observer = new IntersectionObserver(entries => {
          if (entries.some(entry => entry.isIntersecting)) load();
        });
        state.observer.observe(block);
      }
      return;
    }
    for (const el of owned(block, "[data-ks-live-cloak]")) el.hidden = false;
    updateDirty(block, state.baseline);
    for (const [kind, flag] of [["init", "initialized"], ["visible", "visible"]]) {
      if (state[flag]) continue;
      const element = owned(block, "[data-ks-on-" + kind + "]")[0];
      if (!element) continue;
      const trigger = () => {
        if (!block.isConnected || state[flag]) return;
        state[flag] = true;
        const handler = Number(element.getAttribute("data-ks-on-" + kind));
        send(block, handler, handlerArgs(element, handler), element, { type: kind });
      };
      if (kind === "init") queueMicrotask(trigger);
      else {
        state.observer = new IntersectionObserver(entries => {
          if (!entries.some(entry => entry.isIntersecting)) return;
          state.observer?.disconnect();
          trigger();
        });
        state.observer.observe(element);
      }
    }
  }

  // A click needs the same element under the pointer when it goes down and when it comes up. While a button
  // is held, new HTML for its block waits, so a block that redraws often (on-tick) doesn't swallow clicks.
  var pointerDown = false;
  var pending = Object.create(null);

  function flush(id) {
    var p = pending[id];
    if (!p) return;
    var el = document.querySelector('[data-ks-live="' + CSS.escape(id) + '"]');
    if (el && lifecycle.get(el) === p.mounting && modelForms.composingIn(el)) return;
    delete pending[id];
    if (el && lifecycle.get(el) === p.mounting) swap(el, p.html, p.keepValue, p.checkpoint, p.submitted, p.hop);
  }

  document.addEventListener("pointerdown", function () { pointerDown = true; }, true);
  ["pointerup", "pointercancel"].forEach(function (type) {
    document.addEventListener(type, function () {
      pointerDown = false;
      // After the click this pointerup produces has been handled.
      setTimeout(function () { Object.keys(pending).forEach(flush); }, 0);
    }, true);
  });

  // <span on-tick="..." every="200">: while an element with on-tick is in a block, its handler runs every
  // `every` milliseconds, counted from when the last answer arrived, so slow handlers never pile up.
  function scheduleTick(block) {
    var id = block.getAttribute("data-ks-live");
    clearTimeout(ticks[id]);
    var el = null;
    block.querySelectorAll("[data-ks-on-tick]").forEach(function (t) { if (!el && blockOf(t) === block) el = t; });
    if (!el) return;
    var every = parseFloat(el.getAttribute("data-ks-every")) || 1000;
    ticks[id] = setTimeout(function () {
      var current = document.querySelector('[data-ks-live="' + CSS.escape(id) + '"]');
      if (!current || !current.contains(el)) return;
      var handler = parseInt(el.getAttribute("data-ks-on-tick"), 10);
      send(current, handler, handlerArgs(el, handler), el, { type: "tick" });
    }, every);
  }

  function handlerArgs(el, handler) {
    return el.getAttribute("data-ks-args-" + handler) || el.getAttribute("data-ks-args");
  }

  function blockOf(el) { return el && el.closest ? el.closest("[data-ks-live]") : null; }

  function parentStates(block) {
    const parents = [];
    for (let parent = blockOf(block.parentElement); parent; parent = blockOf(parent.parentElement))
      parents.unshift({ id: parent.getAttribute("data-ks-live"), state: parent.getAttribute("data-ks-state") });
    return parents.length ? parents : undefined;
  }

  function uploadAttachments(block, target, type) {
    const selector = "input[type=file][data-ks-live-upload][name]";
    const inputs = type === "submit" && target instanceof HTMLFormElement
      ? Array.from(target.elements).filter(el => el.matches(selector))
      : type === "change" && target?.matches?.(selector) ? [target] : [];
    return inputs.filter(input => blockOf(input) === block && !input.matches(":disabled"))
      .flatMap(input => Array.from(input.files, file => [input.name, file]));
  }

  function emitEvents(block, hop) {
    const encoded = block.getAttribute("data-ks-events");
    block.removeAttribute("data-ks-events");
    if (!encoded || hop >= 16) return;
    for (const effect of JSON.parse(encoded)) {
      for (const recipient of document.querySelectorAll("[data-ks-live]")) {
        if (effect.target !== null && recipient.id !== effect.target) continue;
        const listener = owned(recipient, "[data-ks-on-event]").find(el =>
          !el.hasAttribute("event-name") || el.getAttribute("event-name") === effect.name);
        if (!listener) continue;
        const handler = Number(listener.getAttribute("data-ks-on-event"));
        send(recipient, handler, handlerArgs(listener, handler), listener, {
          type: "component", name: effect.name, data: effect.data, source: block.id, hop: hop + 1
        });
      }
    }
  }

  function download(block) {
    const encoded = block.getAttribute("data-ks-download");
    if (!encoded) return;
    block.removeAttribute("data-ks-download");
    const file = JSON.parse(encoded);
    const contents = atob(file.bytes);
    const data = new Uint8Array(contents.length);
    for (let i = 0; i < contents.length; i++) data[i] = contents.charCodeAt(i);
    const url = URL.createObjectURL(new Blob([data], { type: "application/octet-stream" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = file.name;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  function controlValue(input) {
    if (input.type === "checkbox") return input.checked;
    if (input.type === "select-multiple") return Array.from(input.selectedOptions, option => option.value);
    return "value" in input ? input.value : null;
  }

  function binds(block) {
    var out = Object.create(null);
    block.querySelectorAll("[data-ks-bind]").forEach(function (input) {
      if (blockOf(input) !== block || input.matches(":disabled") || input.type === "radio" && !input.checked) return;
      out[input.getAttribute("data-ks-bind")] = controlValue(input);
    });
    return out;
  }

  function restoreBindings(block, fresh, submitted) {
    const current = binds(block);
    for (const input of owned(fresh, "[data-ks-bind]")) {
      const name = input.getAttribute("data-ks-bind");
      if (!(name in current) || input.matches(":disabled")) continue;
      const value = current[name];
      if (JSON.stringify(value) === JSON.stringify(submitted[name])) continue;
      if (input.type === "checkbox") input.checked = value;
      else if (input.type === "radio") input.checked = value === input.value;
      else if (input.type === "select-multiple") {
        for (const option of input.options) option.selected = value.includes(option.value);
      } else if (input.type !== "file") input.value = value;
    }
  }

  // Events for one block run one at a time, each starting from the state the last one returned.
  var queues = Object.create(null);

  function send(block, handler, args, target, domEvent) {
    var id = block.getAttribute("data-ks-live");
    var mounting = lifecycle.get(block);
    var hop = domEvent?.hop ?? 0;
    const uploads = uploadAttachments(block, target, domEvent?.type);
    clearTimeout(timers[id]);
    var eventInfo = {
      type: domEvent ? domEvent.type : "",
      value: target && "value" in target ? target.value : null,
      checked: target && target.type === "checkbox" ? target.checked : null,
      target_id: target && target.id ? target.id : null,
      key: domEvent?.key ?? null,
      code: domEvent?.code ?? null,
      alt: !!domEvent?.altKey,
      ctrl: !!domEvent?.ctrlKey,
      meta: !!domEvent?.metaKey,
      shift: !!domEvent?.shiftKey,
      repeat: !!domEvent?.repeat,
      client_x: domEvent?.clientX ?? null,
      client_y: domEvent?.clientY ?? null,
      page_x: domEvent?.pageX ?? null,
      page_y: domEvent?.pageY ?? null,
      button: domEvent?.button ?? null,
      pointer_id: domEvent?.pointerId ?? null,
      pointer_type: domEvent?.pointerType ?? null,
      drag_value: domEvent?.type === "drop" ? (domEvent.dataTransfer?.getData(DRAG_VALUE) || null) : null,
    };
    if (domEvent?.type === "component") {
      eventInfo.name = domEvent.name;
      eventInfo.data = domEvent.data;
      eventInfo.source = domEvent.source;
    }
    if (domEvent?.type === "sort") {
      eventInfo.item = domEvent.item;
      eventInfo.from = domEvent.from;
      eventInfo.index = domEvent.index;
      eventInfo.order = domEvent.order;
    }
    var targetBind = target?.getAttribute?.("data-ks-bind");
    if (targetBind && (target.matches(":disabled") || target.type === "radio" && !target.checked)) targetBind = null;
    var targetValue = targetBind ? controlValue(target) : null;

    queues[id] = (queues[id] || Promise.resolve()).then(function () {
      flush(id); // start from the newest HTML, even if it was waiting for the pointer
      var current = document.querySelector('[data-ks-live="' + CSS.escape(id) + '"]');
      if (!current || lifecycle.get(current) !== mounting) return;
      if (current.hasAttribute("data-ks-expired")) {
        if (!modelForms.keepExpiredDraft(document)) location.reload();
        return;
      }
      var checkpoint = modelForms.checkpoint(current);
      var b = binds(current);
      // The input that fired wins over other inputs bound to the same name.
      if (targetBind) b[targetBind] = targetValue;
      showError(current);
      setBusy(current, true);
      var route = current.getAttribute("data-ks-route");
      var headers = { "Content-Type": "application/json" };
      if (route) headers["X-KScript-Live"] = route;
      let body = JSON.stringify({
        page: renderedUrl,
        block: id,
        handler: handler,
        state: current.getAttribute("data-ks-state"),
        parents: parentStates(current),
        args: args || null,
        binds: b,
        event: eventInfo,
      });
      if (route && uploads.length) {
        const form = new FormData();
        form.set("state", body);
        for (const [field, file] of uploads) form.append(field, file);
        delete headers["Content-Type"];
        body = form;
      }
      return fetch(route ? renderedUrl : "/_ks/live", {
        method: "POST", mode: "same-origin", credentials: "same-origin", redirect: "manual",
        headers, body,
      }).then(function (res) {
        if (res.status === 409 || res.type === "opaqueredirect" || (route && res.ok && res.headers.get("X-KScript-Fragment") !== "1")) {
          if (modelForms.keepExpiredDraft(document)) {
            current.setAttribute("data-ks-expired", "");
            setBusy(current, false);
            clearTimeout(ticks[id]);
            delete pending[id];
          } else location.reload();
          return null;
        }
        return res.text().then(function (html) { return { ok: res.ok, status: res.status, html: html }; });
      }).then(function (r) {
        var el = document.querySelector('[data-ks-live="' + CSS.escape(id) + '"]');
        if (!r || !el || lifecycle.get(el) !== mounting) return;
        if (!r.ok) {
          console.error("KScript:", r.html);
          setBusy(el, false);
          showError(el, uploads.length && r.status === 413 ? "The upload is too large." : "The action failed. Try again.");
          scheduleTick(el);
          return;
        }
        // While typing, keep what is in the box; after an action (Add, Reset…) show what the server set.
        var keepValue = handler < 0 || eventInfo.type === "input";
        if (pointerDown || modelForms.composingIn(el)) pending[id] = { html: r.html, keepValue: keepValue, checkpoint: checkpoint, submitted: b, mounting, hop };
        else swap(el, r.html, keepValue, checkpoint, b, hop);
      });
    }).catch(function (err) {
      console.error("KScript:", err);
      var current = document.querySelector('[data-ks-live="' + CSS.escape(id) + '"]');
      if (current && lifecycle.get(current) === mounting) {
        setBusy(current, false);
        showError(current, "Could not reach the server. Try again.");
        scheduleTick(current);
      }
    });
  }

  // Replace the block, keeping focus, caret and what the user is typing.
  function swap(block, html, keepValue, checkpoint, submitted, hop) {
    var active = document.activeElement;
    var keep = null;
    if (active && block.contains(active)) {
      keep = {
        bind: active.getAttribute("data-ks-bind"),
        id: active.id,
        value: active.value,
        start: active.selectionStart,
        end: active.selectionEnd
      };
    }
    var tpl = document.createElement("template");
    tpl.innerHTML = html.trim();
    var fresh = tpl.content.firstElementChild;
    const redirect = fresh.getAttribute("data-ks-redirect");
    if (redirect) {
      const url = new URL(redirect, location.origin);
      if (url.origin === location.origin) {
        location.assign(url.href);
        return;
      }
    }
    var previousQuery = block.getAttribute("data-ks-query");
    var nextQuery = fresh.getAttribute("data-ks-query");
    if (fresh.hasAttribute("data-ks-live-preserve")) {
      for (const name of ["data-ks-state", "data-ks-route", "data-ks-query", "data-ks-events", "data-ks-download"]) {
        if (fresh.hasAttribute(name)) block.setAttribute(name, fresh.getAttribute(name));
        else block.removeAttribute(name);
      }
      const state = lifecycle.get(block);
      if (state) state.baseline = { ...state.baseline, ...submitted };
      setBusy(block, false);
      updateDirty(block, state?.baseline);
      syncQuery(previousQuery, nextQuery);
      scheduleTick(block);
      emitEvents(block, hop);
      download(block);
      return;
    }
    const nested = Array.from(block.querySelectorAll("[data-ks-live]"));
    restoringFocus = true;
    try {
      setBusy(block, false);
      var modelFocus = modelForms.restore(block, fresh, checkpoint, active);
      const state = lifecycle.get(block);
      if (state) state.baseline = binds(fresh);
      restoreBindings(block, fresh, submitted);
      fresh = liveDom(block, fresh);
      syncQuery(previousQuery, nextQuery);
      scheduleTick(fresh);
      startLifecycle(fresh, state);
      for (const old of nested) {
        if (old.isConnected) continue;
        lifecycle.get(old)?.observer?.disconnect();
        clearTimeout(ticks[old.getAttribute("data-ks-live")]);
      }
      for (const child of fresh.querySelectorAll("[data-ks-live]")) {
        if (lifecycle.has(child)) continue;
        scheduleTick(child);
        startLifecycle(child);
      }
      updateOnline();
      emitEvents(fresh, hop);
      download(fresh);
      if (!keep || (active.form?.hasAttribute("data-ks-model-form") && !modelFocus)) return;
      var el = modelFocus || (keep.bind
        ? fresh.querySelector('[data-ks-bind="' + CSS.escape(keep.bind) + '"]')
        : keep.id ? document.getElementById(keep.id) : null);
      if (!el) return;
      if (!modelFocus && keepValue && keep.bind && "value" in el && el.type !== "checkbox") el.value = keep.value;
      el.focus();
      try { if (keep.start != null) el.setSelectionRange(keep.start, keep.end); } catch (e) { /* not a text input */ }
    } finally {
      restoringFocus = false;
    }
  }

  function handle(e) {
    var type = e.type;
    var target = e.target;
    var block = blockOf(target);
    if (restoringFocus && (type === "focusin" || type === "focusout")) return;
    if (modelForms.handle(e, block)) return;
    if (type === "compositionend") setTimeout(function () { Object.keys(pending).forEach(flush); }, 0);
    if (type === "compositionstart" || type === "compositionend" || e.isComposing) return;
    if (!block) return;
    if (type === "input" || type === "change") updateDirty(block, lifecycle.get(block)?.baseline);
    if (liveSort.handle(e, block, (container, details) => {
      const handler = Number(container.getAttribute("data-ks-on-sort"));
      send(block, handler, handlerArgs(container, handler), container, details);
    })) return;
    if (type === "dragstart") {
      var source = target.closest?.("[drag-value]");
      if (source && blockOf(source) === block && e.dataTransfer) {
        e.dataTransfer.setData(DRAG_VALUE, source.getAttribute("drag-value").slice(0, 4096));
      }
      return;
    }
    if (type === "dragover") {
      var drop = target.closest?.("[data-ks-on-drop]");
      if (drop && blockOf(drop) === block && e.dataTransfer?.types.includes(DRAG_VALUE)) e.preventDefault();
      return;
    }
    var el = target.closest ? target.closest("[data-ks-on-" + type + "]") : null;
    if (type === "keydown" && !el) {
      var shortcut = e.key === "Enter" ? "enter" : e.key === "Escape" ? "escape" : null;
      if (shortcut) {
        el = target.closest?.("[data-ks-on-" + shortcut + "]");
        if (el) type = shortcut;
      }
    }

    if (el && blockOf(el) === block) {
      var prevent = el.getAttribute("prevent-default");
      if (type === "submit" || type === "enter" || type === "drop" || (type === "click" && el.tagName === "A") ||
          (prevent !== null && (prevent === "" || prevent.split(/[,\s]+/).includes(e.type)))) e.preventDefault();
      var confirmation = el.getAttribute("data-ks-live-confirm");
      if (confirmation !== null && !window.confirm(confirmation)) {
        e.preventDefault();
        return;
      }
      var handler = parseInt(el.getAttribute("data-ks-on-" + type), 10);
      var args = handlerArgs(el, handler);
      if (type === "input") {
        clearTimeout(timers[block.getAttribute("data-ks-live")]);
        timers[block.getAttribute("data-ks-live")] = setTimeout(function () { send(blockOf(target) || block, handler, args, target, e); }, 120);
      } else {
        send(block, handler, args, target, e);
      }
      return;
    }

    // A binding without an event handler can update immediately, on change, or when focus leaves the field.
    if (target.hasAttribute?.("data-ks-bind") && !target.matches(":disabled") &&
        (target.type !== "radio" || target.checked)) {
      var mode = target.getAttribute("data-ks-bind-on") ||
        (target.tagName === "SELECT" || target.type === "checkbox" || target.type === "radio" ? "change" : "input");
      if (type !== (mode === "blur" ? "focusout" : mode)) return;
      var key = block.getAttribute("data-ks-live");
      clearTimeout(timers[key]);
      timers[key] = setTimeout(function () { send(blockOf(target) || block, -1, null, target, e); }, type === "input" ? 120 : 0);
    }
    if (type === "submit") e.preventDefault();
  }

  EVENTS.forEach(function (type) { document.addEventListener(type, handle, true); });

  function start() {
    document.querySelectorAll("[data-ks-live]").forEach(block => {
      scheduleTick(block);
      startLifecycle(block);
    });
    updateOnline();
  }
  window.addEventListener("online", updateOnline);
  window.addEventListener("offline", updateOnline);
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
  else start();
})();
