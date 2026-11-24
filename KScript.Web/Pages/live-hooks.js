const liveHooks = (() => {
  const listeners = new Map();
  const directives = new Map();
  const clientActions = new Map();
  const installed = new Map();

  function register(registry, name, callback) {
    if (typeof callback !== "function" || typeof name !== "string" || !name)
      throw new TypeError("A live extension needs a name and a function.");
    registry.set(name, callback);
    return () => {
      if (registry.get(name) === callback) registry.delete(name);
    };
  }

  function on(name, callback) {
    if (typeof callback !== "function" || typeof name !== "string" || !name)
      throw new TypeError("A live hook needs a name and a function.");
    const list = listeners.get(name) || new Set();
    list.add(callback);
    listeners.set(name, list);
    return () => {
      list.delete(callback);
      if (!list.size) listeners.delete(name);
    };
  }

  function invoke(callback, detail) {
    try { return callback(detail); }
    catch (error) { console.error("KScript live extension:", error); }
  }

  function emit(name, detail) {
    for (const listener of [...(listeners.get(name) || [])]) invoke(listener, detail);
  }

  function remove(element, active) {
    for (const entry of active.values()) {
      entry.controller.abort();
      if (typeof entry.cleanup === "function") invoke(entry.cleanup);
    }
    installed.delete(element);
  }

  function sync() {
    for (const [element, active] of installed) {
      const component = element.isConnected && livePortals.owner(element);
      for (const [name, entry] of active) {
        if (component === entry.component && element.getAttribute("live-use-" + name) === entry.value &&
            directives.get(name) === entry.callback) continue;
        entry.controller.abort();
        if (typeof entry.cleanup === "function") invoke(entry.cleanup);
        active.delete(name);
      }
      if (!active.size) installed.delete(element);
    }
    for (const [name, callback] of directives) {
      const attribute = "live-use-" + name;
      for (const element of document.querySelectorAll("[" + attribute + "]")) {
        const component = livePortals.owner(element);
        if (!component) continue;
        const active = installed.get(element) || new Map();
        if (active.has(name)) continue;
        const controller = new AbortController();
        const value = element.getAttribute(attribute);
        const entry = { component, value, callback, controller, cleanup: null };
        active.set(name, entry);
        installed.set(element, active);
        entry.cleanup = invoke(callback, { element, component, value, signal: controller.signal });
      }
    }
  }

  return {
    emit, sync,
    clear: () => { for (const [element, active] of installed) remove(element, active); },
    invokeAction: (name, detail) => {
      const action = clientActions.get(name);
      if (!action) return false;
      Promise.resolve(invoke(action, detail)).catch(error => console.error("KScript live action:", error));
      return true;
    },
    api: {
      on,
      directive(name, callback) {
        if (!/^[a-z][a-z0-9-]*$/.test(name)) throw new TypeError("Use a lowercase name for a live directive.");
        const unregister = register(directives, name, callback);
        if (document.readyState !== "loading") sync();
        return () => { unregister(); sync(); };
      },
      action: (name, callback) => register(clientActions, name, callback),
    },
  };
})();
