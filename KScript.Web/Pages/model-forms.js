// Private to /_ks/live.js. Both native and enhanced saves use the generated model binding.
const modelForms = (function () {
  "use strict";
  const selector = "form[data-ks-model-form]";
  const states = new WeakMap();
  let composing = null;

  function stateOf(form) {
    if (!states.has(form)) {
      states.set(form, { form, revision: 0, saved: -1, changes: new Map(), message: "" });
    }
    return states.get(form);
  }

  function status(state, message) {
    state.message = message;
    const output = state.form.querySelector("[data-ks-save-status]");
    if (output && output.textContent !== message) output.textContent = message;
  }

  function changed(input, state) {
    if (!input.name || input.disabled || input.readOnly || input.type === "hidden") return;
    const value = JSON.stringify(new FormData(state.form).getAll(input.name));
    if (state.changes.get(input.name)?.value === value) return;
    state.changes.set(input.name, { value, revision: ++state.revision });
    status(state, "Unsaved changes");
    schedule(state);
  }

  function schedule(state) {
    clearTimeout(state.timer);
    if (state.form.hasAttribute("data-ks-autosave") && composing?.form !== state.form &&
        state.changes.size && state.revision > state.saved) {
      state.timer = setTimeout(() => save(state), Number(state.form.dataset.ksAutosave));
    }
  }

  function payload(state, submitter) {
    const form = state.form;
    const data = submitter ? new FormData(form, submitter) : new FormData(form);
    // Autosave submits only the edited fields. A Save with no edits uses normal form controls.
    const edited = name => state.revision === 0 || state.changes.get(name)?.revision > state.saved;
    const body = new URLSearchParams();
    for (const [name, value] of data) {
      if (name !== "_key" && name !== "_token" && name !== "_return" && !edited(name) && name !== submitter?.name) continue;
      if (typeof value !== "string") throw new Error("File uploads need a separate upload form.");
      body.append(name, value);
    }
    // Native FormData omits unchecked controls. A model's single checkbox represents a boolean.
    for (const input of form.querySelectorAll("input[type=checkbox][name]:not(:disabled)")) {
      if (edited(input.name) && !input.checked && !body.has(input.name)) body.set(input.name, "false");
    }
    return body;
  }

  async function save(state, submitter, manual = false) {
    const form = state.form;
    clearTimeout(state.timer);
    if (!form.isConnected || composing?.form === form || !form.checkValidity()) return;
    // A named submit button can be a distinct model change (e.g. status=published).
    const button = submitter?.name ? { name: submitter.name, value: submitter.value, type: submitter.type } : null;
    const action = JSON.stringify(button);
    if (state.busy) {
      if (state.revision !== state.busy.revision || button && action !== state.busy.action)
        state.queued = { button: button || state.queued?.button, manual: manual || state.queued?.manual };
      return;
    }
    if (state.saved === state.revision && (!button || action === state.lastAction)) return;
    if (button) {
      submitter = Array.from(form.elements).find(el => el.name === button.name && el.type === button.type &&
        el.value === button.value && !el.matches(":disabled"));
      if (!submitter) return;
    }
    const revision = state.revision;
    state.busy = { revision, action };
    status(state, "Saving…");
    try {
      const action = new URL(form.getAttribute("action"), document.baseURI);
      if (action.origin !== location.origin) throw new Error("The save address must be on this site.");
      const body = payload(state, button ? submitter : null);
      if (!Array.from(body.keys()).some(name => !["_key", "_token", "_return"].includes(name))) {
        status(state, "There are no editable changes to save.");
        return;
      }
      const response = await fetch(action, {
        method: "POST", mode: "same-origin", credentials: "same-origin", redirect: "error",
        headers: { "X-KScript-Model-Form": "save", "Accept": "application/json" },
        body
      });
      if (response.status !== 204) {
        if (response.status === 401 || response.status === 419) throw new Error("Sign in again to save your changes.");
        if (response.status === 404) throw new Error("This record is no longer editable.");
        if (response.status === 422 && response.headers.get("Content-Type")?.startsWith("text/plain"))
          throw new Error(await response.text());
        throw new Error("Could not save. Try again.");
      }
      state.saved = revision;
      state.lastAction = action;
      status(state, state.revision === revision ? "Saved" : "Unsaved changes");
    } catch (error) {
      const message = error instanceof TypeError ? "Could not save. Try again." : error.message;
      status(state, state.revision === revision ? message : "Unsaved changes");
    } finally {
      state.busy = null;
      const queued = state.queued;
      state.queued = null;
      if (queued && (queued.manual || state.form.hasAttribute("data-ks-autosave")))
        save(state, queued.button, queued.manual);
    }
  }

  function handle(event, block) {
    const input = event.target;
    if (!(input instanceof Element)) return false;
    const form = input.matches(selector) ? input : input.form;
    if (!form?.matches(selector)) return false;
    const state = stateOf(form);
    if (event.type === "submit") {
      if (!block && !form.hasAttribute("data-ks-autosave")) return false;
      event.preventDefault();
      save(state, event.submitter, true);
      return true;
    }
    if (event.type === "compositionstart") {
      composing = input;
      clearTimeout(state.timer);
    } else if (event.type === "compositionend") {
      composing = null;
      changed(input, state);
      schedule(state);
    } else if ((event.type === "input" || event.type === "change") && !event.isComposing && input !== composing) {
      changed(input, state);
    }
    return false;
  }

  function checkpoint(block) {
    return new Map(livePortals.query(block, selector)
      .filter(form => livePortals.owner(form) === block).map(form => {
      const state = stateOf(form);
      return [state, state.saved];
    }));
  }

  function identity(form) {
    return JSON.stringify([form.getAttribute("action"), form.getAttribute("id"),
      form.querySelector('input[type=hidden][name="_key"]')?.value,
      form.querySelector('input[type=hidden][name="_token"]')?.value]);
  }

  // Reapply drafts only to matching controls in the newly authorized markup. Hidden metadata, disabled
  // controls, field choices and removed records always come from the new server response.
  function restore(block, fresh, checkpoint, active) {
    const incoming = livePortals.query(fresh, selector).filter(form =>
      livePortals.owner(form) === fresh && !form.closest("[data-ks-live-ignore]"));
    let focus = null;
    for (const old of livePortals.query(block, selector)) {
      if (livePortals.owner(old) !== block) continue;
      const state = states.get(old);
      if (!state && active?.form !== old) continue;
      const oldIdentity = identity(old);
      const index = incoming.findIndex(form => identity(form) === oldIdentity);
      if (index < 0) continue;
      const next = incoming.splice(index, 1)[0];
      if (state) {
        states.set(next, state);
        state.form = next;
        if (old.getAttribute("data-ks-autosave") !== next.getAttribute("data-ks-autosave")) schedule(state);
        status(state, state.message);
      }
      const editable = new Set();
      for (const control of old.elements) {
        if (!control.name || control.type === "hidden" || control.type === "file") continue;
        const replacement = Array.from(next.elements).find(el => el.name === control.name && el.type === control.type &&
          (!["checkbox", "radio"].includes(el.type) || el.value === control.value));
        if (!replacement || replacement.matches(":disabled")) continue;
        if (control === active) focus = replacement;
        if (!replacement.readOnly) editable.add(control.name);
        const edit = state?.changes.get(control.name);
        if (!edit || replacement.readOnly) continue;
        if (edit.revision <= (checkpoint?.get(state) ?? -1)) {
          state.changes.delete(control.name);
          continue;
        }
        if (control.type === "checkbox" || control.type === "radio") replacement.checked = control.checked;
        else if (control.type === "select-multiple") {
          const selected = new Set(Array.from(control.selectedOptions, option => option.value));
          for (const option of replacement.options) option.selected = selected.has(option.value);
        } else replacement.value = control.value;
      }
      if (state) {
        for (const name of state.changes.keys()) if (!editable.has(name)) state.changes.delete(name);
      }
    }
    return focus;
  }

  function keepExpiredDraft(block) {
    let keep = false;
    for (const form of block.querySelectorAll(selector)) {
      const state = states.get(form);
      if (!state || !(state.busy || composing?.form === form || state.changes.size && state.revision > state.saved)) continue;
      status(state, "This page expired. Copy your draft before reloading.");
      keep = true;
    }
    return keep;
  }

  return { handle, checkpoint, restore, keepExpiredDraft, composingIn: block => composing && livePortals.owner(composing) === block };
}());
