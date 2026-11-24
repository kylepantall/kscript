// Keep DOM identity across component updates. Model forms own their control state
// and transfer it to the freshly rendered form before reconciliation.
const liveDom = (() => {
  const key = node => node.nodeType === Node.ELEMENT_NODE && (node.getAttribute("data-ks-live-key") || node.id) || null;

  function compatible(current, next) {
    return current.nodeType === next.nodeType &&
      current.nodeName === next.nodeName && current.namespaceURI === next.namespaceURI &&
      (current.nodeType !== Node.ELEMENT_NODE ||
        current.getAttribute("is") === next.getAttribute("is") &&
        !next.hasAttribute("data-ks-live-replace") && !next.hasAttribute("data-ks-model-form"));
  }

  function attributes(current, next) {
    for (const attr of Array.from(current.attributes))
      if (!next.hasAttributeNS(attr.namespaceURI, attr.localName))
        current.removeAttributeNS(attr.namespaceURI, attr.localName);
    for (const attr of next.attributes)
      if (current.getAttributeNS(attr.namespaceURI, attr.localName) !== attr.value)
        current.setAttributeNS(attr.namespaceURI, attr.name, attr.value);
  }

  function children(current, next) {
    const keyed = new Map();
    const unkeyed = [];
    for (const child of current.childNodes) {
      const id = key(child);
      if (id) keyed.set(id, keyed.has(id) ? null : child);
      else unkeyed.push(child);
    }
    let unkeyedIndex = 0;
    let position = current.firstChild;
    for (const replacement of Array.from(next.childNodes)) {
      const id = key(replacement);
      const original = id ? keyed.get(id) : unkeyed[unkeyedIndex++];
      if (id) keyed.delete(id);
      if (original && compatible(original, replacement)) {
        if (original !== position) current.insertBefore(original, position);
        if (!(original.nodeType === Node.ELEMENT_NODE && original.hasAttribute("data-ks-live") &&
              replacement.hasAttribute("data-ks-live"))) update(original, replacement);
        position = original.nextSibling;
      } else {
        current.insertBefore(replacement, position);
        if (original) {
          if (original === position) position = original.nextSibling;
          original.remove();
        }
      }
    }
    while (position) {
      const next = position.nextSibling;
      position.remove();
      position = next;
    }
  }

  function update(current, next) {
    if (current.nodeType !== Node.ELEMENT_NODE) {
      if (current.nodeValue !== next.nodeValue) current.nodeValue = next.nodeValue;
      return;
    }
    if (current.hasAttribute("data-ks-live-ignore") && next.hasAttribute("data-ks-live-ignore")) return;
    attributes(current, next);
    if (next instanceof HTMLTemplateElement) {
      children(current.content, next.content);
      return;
    }
    const selection = next instanceof HTMLSelectElement
      ? Array.from(next.options, option => option.selected) : null;
    children(current, next);
    if (selection) {
      if (current.multiple) {
        for (let i = 0; i < selection.length; i++) current.options[i].selected = selection[i];
      } else current.selectedIndex = selection.indexOf(true);
    }
    if (next instanceof HTMLInputElement) {
      current.checked = next.checked;
      if (next.type !== "file" && current.value !== next.value) current.value = next.value;
    }
    if (next instanceof HTMLTextAreaElement && current.value !== next.value) current.value = next.value;
  }

  return function (current, next) {
    if (!compatible(current, next)) {
      current.replaceWith(next);
      return next;
    }
    update(current, next);
    return current;
  };
})();
