// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The Elgato SDK calls this global once the PI's websocket handshake is ready. `settings` is this
// key's per-action Settings blob; the PI can't hold the bearer token itself (streamdeck-plugin.md P5)
// so any authed read (device/playlist lists) is relayed through the plugin process via sendToPlugin.
let piSocket = null;
let piContext = null;
let piActionInfo = null;

window.connectElgatoStreamDeckSocket = function (port, uuid, event, info, actionInfo) {
  piContext = uuid;
  piActionInfo = JSON.parse(actionInfo);
  piSocket = new WebSocket(`ws://127.0.0.1:${port}`);
  piSocket.onopen = () => {
    piSocket.send(JSON.stringify({ event, uuid }));
    document.dispatchEvent(new CustomEvent("pi:ready", { detail: piActionInfo.payload?.settings ?? {} }));
  };
  piSocket.onmessage = (msg) => {
    const data = JSON.parse(msg.data);
    if (data.event === "sendToPropertyInspector") {
      document.dispatchEvent(new CustomEvent("pi:message", { detail: data.payload }));
    }
  };
};

function setSettings(partial) {
  const settings = { ...(piActionInfo.payload?.settings ?? {}), ...partial };
  piActionInfo.payload.settings = settings;
  piSocket.send(
    JSON.stringify({
      event: "setSettings",
      context: piContext,
      payload: settings,
    }),
  );
}

function requestFromPlugin(request) {
  piSocket.send(
    JSON.stringify({
      event: "sendToPlugin",
      context: piContext,
      payload: request,
    }),
  );
}

/**
 * Every action gets the same "Key color" background picker (keyRenderer.js's DEFAULT_BACKGROUND).
 * A plain native <input type="color">, not sdpi-color: sdpi-color is a Lit-based custom element
 * whose shadow-DOM input binds `.defaultValue` (not `.value`) to its reactive property on render,
 * so an externally-set `.value` can be silently dropped or overwritten by the component's own next
 * render cycle — exactly the "picker shows blank / doesn't stick" symptom. The native input has none
 * of that indirection: its `value` property is standard, synchronous, and always visible.
 */
function initBackgroundColorPicker(container, settings) {
  const item = document.createElement("sdpi-item");
  item.setAttribute("label", "Key color");
  const picker = document.createElement("input");
  picker.type = "color";
  picker.style.cssText = "width:100%;height:28px;border:none;border-radius:4px;cursor:pointer;background:transparent;padding:0";
  picker.value = settings.backgroundColor || "#1a1a1a";
  const apply = (ev) => setSettings({ backgroundColor: ev.target.value });
  picker.addEventListener("input", apply);
  picker.addEventListener("change", apply);
  item.appendChild(picker);
  container.appendChild(item);
}

/**
 * Every action's property inspector shows this FIRST, always — not a separate key the operator has
 * to remember to add. While disconnected it's the whole page (host field + status + approval link);
 * the moment the device flow reports paired, it collapses to one "Connected" line and reveals that
 * key's own normal settings underneath. `normalContent` is the container the caller's own fields
 * already live in — hidden until paired, shown once connected.
 */
function initConnectionGate(gateContainer, normalContent) {
  gateContainer.innerHTML = `
    <sdpi-item label="Host">
      <sdpi-textfield id="gateHost" placeholder="http://localhost:5080"></sdpi-textfield>
    </sdpi-item>
    <sdpi-item label="Status">
      <span id="gateStatus">Checking connection…</span>
    </sdpi-item>
    <sdpi-item id="gateLinkItem" style="display: none">
      <a id="gateLink" href="#" target="_blank" rel="noopener">Open approval page</a>
    </sdpi-item>
  `;
  normalContent.style.display = "none";

  requestFromPlugin({ type: "getHost" });
  requestFromPlugin({ type: "getPairingStatus" });

  document.getElementById("gateHost").addEventListener("change", (change) => {
    requestFromPlugin({ type: "setHost", host: change.target.value });
    applyGateStatus({ paired: false, verificationUri: null, lastError: null });
  });

  function applyGateStatus(msg) {
    const linkItem = document.getElementById("gateLinkItem");
    const link = document.getElementById("gateLink");
    const status = document.getElementById("gateStatus");
    if (msg.paired) {
      status.textContent = `Connected${msg.tokenExpiresAt ? ` (expires ${new Date(msg.tokenExpiresAt).toLocaleDateString()})` : ""}`;
      linkItem.style.display = "none";
      gateContainer.style.display = "none";
      normalContent.style.display = "";
    } else {
      gateContainer.style.display = "";
      normalContent.style.display = "none";
      if (msg.verificationUri) {
        status.textContent = "Waiting for approval…";
        link.href = msg.verificationUri;
        linkItem.style.display = "";
      } else if (msg.lastError) {
        status.textContent = `${msg.lastError} — retrying…`;
        linkItem.style.display = "none";
      } else {
        status.textContent = "Starting pairing…";
        linkItem.style.display = "none";
      }
    }
  }

  document.addEventListener("pi:message", (ev) => {
    const msg = ev.detail;
    if (msg?.type === "host") {
      document.getElementById("gateHost").value = msg.host || "";
    } else if (msg?.type === "pairingStatus") {
      applyGateStatus(msg);
    }
  });
}
