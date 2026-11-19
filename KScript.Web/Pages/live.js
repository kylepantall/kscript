// KScript live runtime. Served by `kscript serve` at /_ks/live.js and added to pages automatically.
// It sends browser events for <live> blocks to the server, which runs the KScript handler and
// returns the block's new HTML. Pages never contain JavaScript of their own.
(function () {
  "use strict";
  var EVENTS = ["click", "input", "change", "submit", "keydown"];
  var timers = {};

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
  var queues = {};

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
      var current = document.querySelector('[data-ks-live="' + id + '"]');
      if (!current) return;
      var b = binds(current);
      // The input that fired wins over other inputs bound to the same name.
      if (targetBind) b[targetBind] = targetValue;
      current.setAttribute("aria-busy", "true");
      return fetch("/_ks/live", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
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
        if (res.status === 409) { location.reload(); return null; }
        return res.text().then(function (html) { return { ok: res.ok, html: html }; });
      }).then(function (r) {
        var el = document.querySelector('[data-ks-live="' + id + '"]');
        if (!r || !el) return;
        if (!r.ok) { console.error("KScript:", r.html); el.removeAttribute("aria-busy"); return; }
        // While typing, keep what is in the box; after an action (Add, Reset…) show what the server set.
        swap(el, r.html, handler < 0 || eventInfo.type === "input");
      });
    }).catch(function (err) {
      console.error("KScript:", err);
    });
  }

  // Replace the block, keeping focus, caret and what the user is typing.
  function swap(block, html, keepValue) {
    var active = document.activeElement;
    var keep = null;
    if (active && blockOf(active) === block) {
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
    block.replaceWith(fresh);
    if (!keep) return;
    var el = keep.bind
      ? fresh.querySelector('[data-ks-bind="' + keep.bind + '"]')
      : keep.id ? document.getElementById(keep.id) : null;
    if (!el) return;
    if (keepValue && keep.bind && "value" in el && el.type !== "checkbox") el.value = keep.value;
    el.focus();
    try { if (keep.start != null) el.setSelectionRange(keep.start, keep.end); } catch (e) { /* not a text input */ }
  }

  function handle(e) {
    var type = e.type;
    var target = e.target;
    var el = target.closest ? target.closest("[data-ks-on-" + type + "]") : null;
    if (type === "keydown" && !el && e.key === "Enter") {
      el = target.closest ? target.closest("[data-ks-on-enter]") : null;
      if (el) type = "enter";
    }
    var block = blockOf(target);
    if (!block) return;

    if (el) {
      if (type === "submit" || type === "enter" || (type === "click" && el.tagName === "A")) e.preventDefault();
      var handler = parseInt(el.getAttribute("data-ks-on-" + type), 10);
      var args = el.getAttribute("data-ks-args");
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
})();
