// KScript live runtime. Served by `kscript serve` at /_ks/live.js and added to pages automatically.
// It sends browser events for <live> blocks to the server, which runs the KScript handler and
// returns the block's new HTML. Pages never contain JavaScript of their own.
(function () {
  "use strict";
  var EVENTS = ["click", "input", "change", "submit", "keydown", "compositionstart", "compositionend"];
  var timers = Object.create(null);
  var ticks = Object.create(null);

  // A click needs the same element under the pointer when it goes down and when it comes up. While a button
  // is held, new HTML for its block waits, so a block that redraws often (on-tick) doesn't swallow clicks.
  var pointerDown = false;
  var pending = Object.create(null);

  function flush(id) {
    var p = pending[id];
    if (!p) return;
    var el = document.querySelector('[data-ks-live="' + CSS.escape(id) + '"]');
    if (el && modelForms.composingIn(el)) return;
    delete pending[id];
    if (el) swap(el, p.html, p.keepValue, p.checkpoint);
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

  function binds(block) {
    var out = {};
    block.querySelectorAll("[data-ks-bind]").forEach(function (input) {
      if (blockOf(input) !== block) return;
      out[input.getAttribute("data-ks-bind")] =
        input.type === "checkbox" ? input.checked : input.value;
    });
    return out;
  }

  // Events for one block run one at a time, each starting from the state the last one returned.
  var queues = Object.create(null);

  function send(block, handler, args, target, domEvent) {
    var id = block.getAttribute("data-ks-live");
    clearTimeout(timers[id]);
    var eventInfo = {
      type: domEvent ? domEvent.type : "",
      value: target && "value" in target ? target.value : null,
      checked: target && target.type === "checkbox" ? target.checked : null,
      key: domEvent && domEvent.key ? domEvent.key : null
    };
    var targetBind = target && target.getAttribute ? target.getAttribute("data-ks-bind") : null;
    var targetValue = target && target.type === "checkbox" ? target.checked : target && "value" in target ? target.value : null;

    queues[id] = (queues[id] || Promise.resolve()).then(function () {
      flush(id); // start from the newest HTML, even if it was waiting for the pointer
      var current = document.querySelector('[data-ks-live="' + CSS.escape(id) + '"]');
      if (!current) return;
      if (current.hasAttribute("data-ks-expired")) {
        if (!modelForms.keepExpiredDraft(document)) location.reload();
        return;
      }
      var checkpoint = modelForms.checkpoint(current);
      var b = binds(current);
      // The input that fired wins over other inputs bound to the same name.
      if (targetBind) b[targetBind] = targetValue;
      current.setAttribute("aria-busy", "true");
      var route = current.getAttribute("data-ks-route");
      var headers = { "Content-Type": "application/json" };
      if (route) headers["X-KScript-Live"] = route;
      return fetch(route ? location.pathname + location.search : "/_ks/live", {
        method: "POST", mode: "same-origin", credentials: "same-origin", redirect: "manual",
        headers: headers,
        body: JSON.stringify({
          page: location.pathname + location.search,
          block: id,
          handler: handler,
          state: current.getAttribute("data-ks-state"),
          args: args || null,
          binds: b,
          event: eventInfo
        })
      }).then(function (res) {
        if (res.status === 409 || res.type === "opaqueredirect" || (route && res.ok && res.headers.get("X-KScript-Fragment") !== "1")) {
          if (modelForms.keepExpiredDraft(document)) {
            current.setAttribute("data-ks-expired", "");
            current.removeAttribute("aria-busy");
            clearTimeout(ticks[id]);
            delete pending[id];
          } else location.reload();
          return null;
        }
        return res.text().then(function (html) { return { ok: res.ok, html: html }; });
      }).then(function (r) {
        var el = document.querySelector('[data-ks-live="' + CSS.escape(id) + '"]');
        if (!r || !el) return;
        if (!r.ok) { console.error("KScript:", r.html); el.removeAttribute("aria-busy"); scheduleTick(el); return; }
        // While typing, keep what is in the box; after an action (Add, Reset…) show what the server set.
        var keepValue = handler < 0 || eventInfo.type === "input";
        if (pointerDown || modelForms.composingIn(el)) pending[id] = { html: r.html, keepValue: keepValue, checkpoint: checkpoint };
        else swap(el, r.html, keepValue, checkpoint);
      });
    }).catch(function (err) {
      console.error("KScript:", err);
      var current = document.querySelector('[data-ks-live="' + CSS.escape(id) + '"]');
      if (current) { current.removeAttribute("aria-busy"); scheduleTick(current); }
    });
  }

  // Replace the block, keeping focus, caret and what the user is typing.
  function swap(block, html, keepValue, checkpoint) {
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
    var modelFocus = modelForms.restore(block, fresh, checkpoint, active);
    block.replaceWith(fresh);
    scheduleTick(fresh);
    if (!keep || (active.form?.hasAttribute("data-ks-model-form") && !modelFocus)) return;
    var el = modelFocus || (keep.bind
      ? fresh.querySelector('[data-ks-bind="' + CSS.escape(keep.bind) + '"]')
      : keep.id ? document.getElementById(keep.id) : null);
    if (!el) return;
    if (!modelFocus && keepValue && keep.bind && "value" in el && el.type !== "checkbox") el.value = keep.value;
    el.focus();
    try { if (keep.start != null) el.setSelectionRange(keep.start, keep.end); } catch (e) { /* not a text input */ }
  }

  function handle(e) {
    var type = e.type;
    var target = e.target;
    var block = blockOf(target);
    if (modelForms.handle(e, block)) return;
    if (type === "compositionend") setTimeout(function () { Object.keys(pending).forEach(flush); }, 0);
    if (type === "compositionstart" || type === "compositionend" || e.isComposing) return;
    var el = target.closest ? target.closest("[data-ks-on-" + type + "]") : null;
    if (type === "keydown" && !el && e.key === "Enter") {
      el = target.closest ? target.closest("[data-ks-on-enter]") : null;
      if (el) type = "enter";
    }
    if (!block) return;

    if (el) {
      if (type === "submit" || type === "enter" || (type === "click" && el.tagName === "A")) e.preventDefault();
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

    // A bound input with no handler of its own still updates the block as you type.
    if ((type === "input" || type === "change") && target.hasAttribute && target.hasAttribute("data-ks-bind")) {
      var key = block.getAttribute("data-ks-live");
      clearTimeout(timers[key]);
      timers[key] = setTimeout(function () { send(blockOf(target) || block, -1, null, target, e); }, type === "input" ? 120 : 0);
    }
    if (type === "submit") e.preventDefault();
  }

  EVENTS.forEach(function (type) { document.addEventListener(type, handle, true); });

  function startTicks() { document.querySelectorAll("[data-ks-live]").forEach(scheduleTick); }
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", startTicks);
  else startTicks();
})();
