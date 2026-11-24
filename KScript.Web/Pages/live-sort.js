// Native drag/drop and a keyboard interaction share one server action.
const liveSort = (() => {
  const type = "application/x-kscript-sort";
  let dragged;

  function items(container) {
    return Array.from(container.children).filter(child => child.hasAttribute("data-ks-sort-item"));
  }

  function move(container, item, index, dispatch) {
    const order = items(container);
    const from = order.indexOf(item);
    if (from < 0 || index < 0 || index >= order.length || index === from) return;
    order.splice(from, 1);
    order.splice(index, 0, item);
    dispatch(container, {
      type: "sort", item: item.getAttribute("data-ks-sort-item"), from, index,
      order: order.map(node => node.getAttribute("data-ks-sort-item"))
    });
  }

  function handle(event, block, dispatch) {
    if (event.type === "dragend" && dragged) {
      dragged = null;
      return true;
    }
    const item = event.target.closest?.("[data-ks-sort-item]");
    const container = item?.parentElement;
    if (!container?.hasAttribute("data-ks-on-sort") || livePortals.owner(container) !== block) return false;
    if (event.type === "dragstart" && event.dataTransfer) {
      dragged = item;
      event.dataTransfer.setData(type, item.getAttribute("data-ks-sort-item"));
      event.dataTransfer.effectAllowed = "move";
      return true;
    }
    if ((event.type === "dragover" || event.type === "drop") && dragged?.parentElement === container &&
        event.dataTransfer?.types.includes(type)) {
      event.preventDefault();
      if (event.type === "drop") {
        const order = items(container).filter(node => node !== dragged);
        const box = item.getBoundingClientRect();
        const horizontal = container.getAttribute("sort-axis") === "horizontal";
        const after = horizontal ? event.clientX > box.left + box.width / 2 : event.clientY > box.top + box.height / 2;
        if (item !== dragged) move(container, dragged, order.indexOf(item) + Number(after), dispatch);
        dragged = null;
      }
      return true;
    }
    if (event.type === "keydown" && event.altKey && !event.shiftKey && !event.ctrlKey && !event.metaKey) {
      const horizontal = container.getAttribute("sort-axis") === "horizontal";
      const previous = horizontal ? "ArrowLeft" : "ArrowUp";
      const next = horizontal ? "ArrowRight" : "ArrowDown";
      if (event.key !== previous && event.key !== next) return false;
      event.preventDefault();
      move(container, item, items(container).indexOf(item) + (event.key === previous ? -1 : 1), dispatch);
      return true;
    }
    return false;
  }

  return { handle };
})();
