// The template is the source of truth. A mounted portal keeps its DOM at the
// destination while actions and bindings still belong to its source component.
const livePortals = (() => {
  const selector = "template[data-ks-live-portal]";
  const mounted = new Map();
  const containers = new WeakMap();
  const fragments = new WeakMap();

  function owner(node) {
    for (let current = node; current;) {
      if (current.nodeType === Node.ELEMENT_NODE && current.hasAttribute("data-ks-live")) return current;
      current = containers.get(current) || fragments.get(current) || current.parentNode;
    }
    return null;
  }

  function query(root, match) {
    const result = new Set(root.querySelectorAll(match));
    for (const source of root.querySelectorAll(selector)) {
      fragments.set(source.content, source);
      for (const node of query(mounted.get(source) || source.content, match)) result.add(node);
    }
    return [...result].filter(node => !root.isConnected || node.isConnected);
  }

  function destination(source) {
    try {
      const target = document.querySelector(source.getAttribute("data-ks-live-portal"));
      return target && (target === document.body || document.body.contains(target)) &&
        !owner(target) && !target.closest("[data-ks-portal-container]") ? target : null;
    } catch {
      return null;
    }
  }

  function unmount(source, wrapper, preserve) {
    mounted.delete(source);
    if (preserve) source.content.append(...wrapper.childNodes);
    wrapper.remove();
  }

  function sync() {
    for (const [source, wrapper] of mounted) {
      if (!source.isConnected || !source.matches(selector)) unmount(source, wrapper, false);
      else if (!destination(source)) unmount(source, wrapper, true);
    }
    const mountQueue = new Set(document.querySelectorAll(selector));
    for (const source of mountQueue) {
      if (!source.isConnected || !owner(source)) continue;
      const target = destination(source);
      if (!target) continue;
      let wrapper = mounted.get(source);
      if (!wrapper) {
        wrapper = document.createElement("div");
        wrapper.setAttribute("data-ks-portal-container", "");
        wrapper.style.display = "contents";
        wrapper.append(source.content);
        mounted.set(source, wrapper);
        containers.set(wrapper, source);
      }
      if (wrapper.parentNode !== target) target.append(wrapper);
      // Descendant templates only become connected after their parent portal mounts.
      for (const child of wrapper.querySelectorAll(selector)) mountQueue.add(child);
    }
  }

  function clear() {
    for (const [source, wrapper] of mounted) unmount(source, wrapper, false);
  }

  return {
    owner, query, sync, clear,
    content(source) {
      const wrapper = mounted.get(source);
      if (wrapper && !source.matches(selector)) unmount(source, wrapper, true);
      return mounted.get(source) || source.content;
    },
  };
})();
