// Included in /_ks/live.js for passkey buttons, including buttons added by live updates.
(function () {
  "use strict";

  function decode(value) {
    const binary = atob(value.replace(/-/g, "+").replace(/_/g, "/"));
    return Uint8Array.from(binary, character => character.charCodeAt(0));
  }

  function encode(buffer) {
    let binary = "";
    for (const byte of new Uint8Array(buffer)) binary += String.fromCharCode(byte);
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  }

  function optionsFromJSON(register, options) {
    const parse = register ? PublicKeyCredential.parseCreationOptionsFromJSON : PublicKeyCredential.parseRequestOptionsFromJSON;
    if (parse) return parse.call(PublicKeyCredential, options);

    // Browsers predating the JSON helpers still accept the WebAuthn buffer fields.
    options.challenge = decode(options.challenge);
    if (register) options.user.id = decode(options.user.id);
    const descriptors = register ? options.excludeCredentials : options.allowCredentials;
    for (const descriptor of descriptors || []) descriptor.id = decode(descriptor.id);
    return options;
  }

  function credentialJSON(credential, register) {
    if (credential.toJSON) return credential.toJSON();
    const response = credential.response;
    const result = {
      id: credential.id, rawId: encode(credential.rawId), type: credential.type,
      clientExtensionResults: credential.getClientExtensionResults(),
      response: { clientDataJSON: encode(response.clientDataJSON) }
    };
    if (register) {
      result.response.attestationObject = encode(response.attestationObject);
      if (response.getTransports) result.response.transports = response.getTransports();
    } else {
      result.response.authenticatorData = encode(response.authenticatorData);
      result.response.signature = encode(response.signature);
      result.response.userHandle = response.userHandle == null ? null : encode(response.userHandle);
    }
    return result;
  }

  async function post(action, step, body) {
    const response = await fetch("/_ks/passkeys/" + action + "/" + step, {
      method: "POST", credentials: "same-origin", mode: "same-origin", redirect: "error",
      headers: { "Content-Type": "application/json", "Accept": "application/json" },
      body: JSON.stringify(body)
    });
    const data = await response.json().catch(() => null);
    if (!response.ok || !data) {
      throw new Error(data && data.error || "The passkey request failed. Reload the page and try again.");
    }
    return data;
  }

  function redirectURL(target) {
    if (target == null || target === "") return null;
    const url = new URL(target, location.href);
    if (typeof target !== "string" || url.origin !== location.origin || url.protocol !== location.protocol || url.username || url.password) {
      throw new Error("The passkey redirect must be on this site and contain no credentials.");
    }
    return url;
  }

  document.addEventListener("click", async function (event) {
    const button = event.target.closest && event.target.closest("button[data-ks-passkey]");
    if (!button || button.disabled) return;
    event.preventDefault();
    const action = button.dataset.ksPasskey;
    if (action !== "register" && action !== "login" && action !== "signup") return;
    const register = action !== "login";
    const status = button.nextElementSibling;
    function announce(message) {
      if (status && status.hasAttribute("data-ks-passkey-status")) status.textContent = " " + message;
    }

    button.disabled = true;
    try {
      if (!window.isSecureContext || !window.PublicKeyCredential || !navigator.credentials) {
        throw new Error("Passkeys need a supported browser and a secure connection (HTTPS or localhost).");
      }
      let redirect = redirectURL(button.dataset.ksPasskeyRedirect);

      announce("Follow the passkey prompt on your device.");
      const publicKey = optionsFromJSON(register, await post(action, "options", {}));
      const credential = register ? await navigator.credentials.create({ publicKey }) : await navigator.credentials.get({ publicKey });
      if (!credential) throw new Error("The passkey prompt was cancelled.");
      const result = await post(action, "verify", credentialJSON(credential, register));
      if (result.ok !== true) throw new Error("The server could not verify the passkey.");

      if (!redirect && action !== "register") redirect = redirectURL(result.redirect);
      announce(action === "register" ? "Passkey added." : "Signed in.");
      if (redirect) location.assign(redirect.href);
      else if (action !== "register") location.reload();
    } catch (error) {
      announce(error.name === "NotAllowedError" ? "The passkey prompt was cancelled or timed out. You can try again." : error.message);
    } finally {
      button.disabled = false;
    }
  });
})();
