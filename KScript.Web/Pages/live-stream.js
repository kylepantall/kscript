const liveStream = (() => {
  const contentType = "application/x-ndjson";

  async function read(response, update) {
    if (!response.body) throw new Error("The live stream has no body.");
    const reader = response.body.getReader();
    const decoder = new TextDecoder("utf-8", { fatal: true });
    let buffered = "";
    let final;

    function frame(line) {
      if (!line.trim()) return;
      if (line.length > 32 * 1024 * 1024) throw new Error("The live stream frame is too large.");
      if (final) throw new Error("The live stream continued after its final response.");
      const data = JSON.parse(line);
      if (data?.type === "stream" && typeof data.target === "string" && typeof data.text === "string" &&
          (data.mode === "append" || data.mode === "replace")) {
        update(data);
      } else if (data?.type === "final" && Number.isInteger(data.status) && typeof data.html === "string") {
        final = data;
      } else throw new Error("Invalid live stream frame.");
    }

    try {
      while (true) {
        const { value, done } = await reader.read();
        buffered += done ? decoder.decode() : decoder.decode(value, { stream: true });
        let end;
        while ((end = buffered.indexOf("\n")) >= 0) {
          frame(buffered.slice(0, end));
          buffered = buffered.slice(end + 1);
        }
        if (buffered.length > 32 * 1024 * 1024) throw new Error("The live stream frame is too large.");
        if (done) break;
      }
      frame(buffered);
      if (!final) throw new Error("The live stream ended before the action finished.");
      return final;
    } catch (error) {
      await reader.cancel().catch(() => {});
      throw error;
    } finally {
      reader.releaseLock();
    }
  }

  function updates(component, targets) {
    const originals = new Map();
    return {
      apply(frame) {
        for (const target of targets) {
          if (!target.isConnected || target.getAttribute("data-ks-live-stream") !== frame.target) continue;
          if (!originals.has(target)) originals.set(target, Array.from(target.childNodes));
          if (frame.mode === "replace") target.replaceChildren();
          if (frame.html === true) target.insertAdjacentHTML("beforeend", frame.text);
          else target.append(document.createTextNode(frame.text));
          liveHooks.emit("stream", { component, element: target, frame });
        }
      },
      restore() {
        for (const [target, nodes] of originals) {
          if (target.isConnected) target.replaceChildren(...nodes);
        }
      },
    };
  }

  return { contentType, read, updates };
})();
