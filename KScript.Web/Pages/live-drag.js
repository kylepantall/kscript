// @drag(...): move an element with the pointer, or drag a ghost copy of it onto an @on-drop target.
// The element keeps a `dragging` class while it moves and `drag-settled` until its animation ends after a drop.
// --drag-vx, --drag-vy and --drag-speed (pixels per frame, smoothed) let a stylesheet tilt or curl it.
const liveDrag = (() => {
  const SKIP = "input, textarea, select, button, a, label, [contenteditable], [data-ks-drag-skip]";
  let api = null;
  let suppressClick = false;

  function options(el) {
    try { return JSON.parse(el.getAttribute("data-ks-drag") || "{}"); } catch { return {}; }
  }

  function scroller(el, selector) {
    if (selector) return el.closest(selector) || document.querySelector(selector);
    for (let node = el.parentElement; node; node = node.parentElement) {
      const style = getComputedStyle(node);
      if (/(auto|scroll)/.test(style.overflow + style.overflowX + style.overflowY)) return node;
    }
    return null;
  }

  function autoScroll(surface, e) {
    if (!surface) return;
    const r = surface.getBoundingClientRect();
    const edge = 40;
    if (e.clientX > r.right - edge) surface.scrollLeft += 14;
    else if (e.clientX < r.left + edge) surface.scrollLeft -= 14;
    if (e.clientY > r.bottom - edge) surface.scrollTop += 14;
    else if (e.clientY < r.top + edge) surface.scrollTop -= 14;
  }

  // Above every other draggable element in the same positioned parent. Each one can be in its own live
  // component, so they are found by their directive rather than as siblings.
  function topZ(el) {
    let top = 0;
    const parent = el.offsetParent || el.parentElement;
    for (const other of parent?.querySelectorAll("[data-ks-drag]") || []) {
      if (other === el) continue;
      const z = parseInt(getComputedStyle(other).zIndex, 10);
      if (z > top) top = z;
    }
    return top + 1;
  }

  function paint(el, vx, vy) {
    el.style.setProperty("--drag-vx", vx.toFixed(2));
    el.style.setProperty("--drag-vy", vy.toFixed(2));
    el.style.setProperty("--drag-speed", Math.hypot(vx, vy).toFixed(2));
  }

  function unpaint(el) {
    for (const name of ["--drag-vx", "--drag-vy", "--drag-speed"]) el.style.removeProperty(name);
  }

  function settle(el) {
    el.classList.remove("drag-settled");
    void el.offsetWidth; // restart the animation
    api.hold(el, "drag-settled", true);
    const done = () => api.hold(el, "drag-settled", false);
    el.addEventListener("animationend", done, { once: true });
    setTimeout(done, 1500);
  }

  function dropTarget(e) {
    const under = document.elementFromPoint(e.clientX, e.clientY);
    const target = under?.closest?.("[data-ks-on-drop]");
    return target && api.blockOf(target) ? target : null;
  }

  // A press on the element (not on one of its controls) starts a drag once the pointer moves past the threshold.
  function start(e, block) {
    if (e.button !== 0 || !e.isPrimary) return false;
    const el = e.target.closest?.("[data-ks-drag]");
    if (!el || api.blockOf(el) !== block) return false;
    const opts = options(el);
    const skipped = e.target.closest(SKIP + (opts.skip ? ", " + opts.skip : ""));
    if (skipped && el.contains(skipped) && skipped !== el) return false;
    if (!opts.copy) {
      e.preventDefault(); // no text selection while dragging
      document.activeElement?.blur?.();
    }

    const surface = scroller(el, opts.scroll);
    const origin = { x: el.offsetLeft, y: el.offsetTop };
    const start = { x: e.clientX + (surface?.scrollLeft || 0), y: e.clientY + (surface?.scrollTop || 0) };
    const threshold = typeof opts.threshold === "number" ? opts.threshold : 4;
    let moved = false, vx = 0, vy = 0, frame = 0, ghost = null, over = null;
    let last = { x: e.clientX, y: e.clientY, t: performance.now() };
    let pos = origin;
    const z = opts.front ? topZ(el) : null;
    const target = opts.copy ? null : el;
    if (target) {
      if (z !== null) target.style.zIndex = z;
      try { target.setPointerCapture(e.pointerId); } catch { /* the pointer is already gone */ }
    }

    const move = ev => {
      if (ev.pointerId !== e.pointerId) return;
      const scrollX = surface?.scrollLeft || 0, scrollY = surface?.scrollTop || 0;
      const dx = ev.clientX + scrollX - start.x, dy = ev.clientY + scrollY - start.y;
      if (!moved && Math.hypot(dx, dy) < threshold) return;
      if (!moved) {
        moved = true;
        if (opts.copy) {
          ghost = document.createElement("div");
          ghost.className = opts.ghost || el.className;
          ghost.setAttribute("aria-hidden", "true");
          ghost.style.cssText = "position: fixed; left: 0; top: 0; pointer-events: none; z-index: 2147483647";
          document.body.append(ghost);
        } else api.hold(el, "dragging", true);
      }
      const now = performance.now();
      const dt = Math.max(8, now - last.t);
      vx = vx * 0.7 + ((ev.clientX - last.x) / dt) * 16 * 0.3;
      vy = vy * 0.7 + ((ev.clientY - last.y) / dt) * 16 * 0.3;
      last = { x: ev.clientX, y: ev.clientY, t: now };
      if (ghost) {
        ghost.style.transform = `translate(${ev.clientX - ghost.offsetWidth / 2}px, ${ev.clientY - 14}px)`;
        const next = dropTarget(ev);
        if (next !== over) {
          api.setDropClass(over, false);
          api.setDropClass(next, true);
          over = next;
        }
      } else {
        pos = { x: Math.round(Math.max(0, origin.x + dx)), y: Math.round(Math.max(0, origin.y + dy)) };
        el.style.left = pos.x + "px";
        el.style.top = pos.y + "px";
        autoScroll(surface, ev);
      }
      if (!frame) frame = requestAnimationFrame(() => {
        frame = 0;
        paint(ghost || el, vx, vy);
      });
    };

    const end = ev => {
      if (ev.pointerId !== e.pointerId) return;
      window.removeEventListener("pointermove", move, true);
      window.removeEventListener("pointerup", end, true);
      window.removeEventListener("pointercancel", end, true);
      cancelAnimationFrame(frame);
      const cancelled = ev.type === "pointercancel";
      if (ghost) {
        ghost.remove();
        api.setDropClass(over, false);
        suppressClick = true;
        setTimeout(() => { suppressClick = false; }, 0);
        const drop = cancelled ? null : dropTarget(ev);
        if (drop) {
          const handler = Number(drop.getAttribute("data-ks-on-drop"));
          api.send(api.blockOf(drop), handler, api.handlerArgs(drop, handler), drop, {
            type: "drop", clientX: ev.clientX, clientY: ev.clientY, pageX: ev.pageX, pageY: ev.pageY,
            button: ev.button, pointerId: ev.pointerId, pointerType: ev.pointerType,
            handlerElement: drop, dragValue: opts.value == null ? null : String(opts.value),
          });
        }
        return;
      }
      if (!opts.copy) {
        unpaint(el);
        if (moved) {
          suppressClick = true;
          setTimeout(() => { suppressClick = false; }, 0);
          api.hold(el, "dragging", false);
          settle(el);
        }
        const handlerAt = el.getAttribute("data-ks-on-dragend");
        if (handlerAt === null || cancelled && !moved) return;
        const handler = Number(handlerAt);
        api.send(block, handler, api.handlerArgs(el, handler), el, {
          type: "dragend", clientX: ev.clientX, clientY: ev.clientY,
          drag: { x: pos.x, y: pos.y, vx: Math.round(vx * 100) / 100, vy: Math.round(vy * 100) / 100, moved, z },
        });
      }
    };

    window.addEventListener("pointermove", move, true);
    window.addEventListener("pointerup", end, true);
    window.addEventListener("pointercancel", end, true);
    return !opts.copy;
  }

  // The click a drag ends with is not a click on the element.
  document.addEventListener("click", e => {
    if (!suppressClick) return;
    suppressClick = false;
    e.preventDefault();
    e.stopImmediatePropagation();
  }, true);

  return { install(hooks) { api = hooks; }, start };
})();
