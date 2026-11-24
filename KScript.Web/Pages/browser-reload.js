(() => {
  const revision = document.currentScript.dataset.revision;
  const events = new EventSource("/_kscript/reload");
  events.onmessage = ({ data }) => {
    if (data === revision) return;
    events.close();
    window.location.reload();
  };
})();
