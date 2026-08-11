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

/** Every action gets the same "Key color" background picker (keyRenderer.js's DEFAULT_BACKGROUND). */
function initBackgroundColorPicker(container, settings) {
  const item = document.createElement("sdpi-item");
  item.setAttribute("label", "Key color");
  const picker = document.createElement("sdpi-color");
  picker.value = settings.backgroundColor || "#1a1a1a";
  picker.addEventListener("change", (ev) => setSettings({ backgroundColor: ev.target.value }));
  item.appendChild(picker);
  container.appendChild(item);
}
