// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The overlay SDK (window.NomNomz) as the editor preview runs it: the same surface as /overlay/sdk.js, with no
// hub behind it. The editor drives it over postMessage (__nnzFire, __nnzSettings), and everything the widget
// does that would reach the bot — an action, a claim, an error — is reported back to the editor as
// __nnzPreview instead of being performed. Injected as a classic script before the widget bundle, after the
// WIDGET_* globals.
(function () {
  var handlers = {};
  var anyHandlers = [];
  var settingsHandlers = [];
  var currentSettings = (window.WIDGET_SETTINGS && typeof window.WIDGET_SETTINGS === "object") ? window.WIDGET_SETTINGS : {};

  function tell(entry) {
    try { window.parent.postMessage({ __nnzPreview: entry }, "*"); } catch (_) {}
  }

  function describe(e) {
    return (e && e.message) || String(e);
  }

  // A throwing handler never stops the others, and the editor shows where it threw.
  function run(where, fn, a, b) {
    try { fn(a, b); } catch (e) { tell({ kind: "error", message: where + ": " + describe(e), stack: (e && e.stack) ? String(e.stack) : "" }); }
  }

  function on(type, fn) { if (typeof fn === "function") (handlers[type] = handlers[type] || []).push(fn); return api; }
  function off(type, fn) { var l = handlers[type]; if (l) handlers[type] = l.filter(function (h) { return h !== fn; }); return api; }
  function onAny(fn) { if (typeof fn === "function") anyHandlers.push(fn); return api; }
  function onSettings(fn) {
    if (typeof fn === "function") { settingsHandlers.push(fn); run("onSettings handler", fn, currentSettings); }
    return api;
  }

  function emit(type, data) {
    (handlers[type] || []).forEach(function (fn) { run("on('" + type + "') handler", fn, data, type); });
    anyHandlers.forEach(function (fn) { run("onAny handler", fn, type, data); });
  }

  function applySettings(s) {
    if (!s || typeof s !== "object") return;
    currentSettings = s;
    settingsHandlers.forEach(function (fn) { run("onSettings handler", fn, currentSettings); });
  }

  // Recorded, never run: the preview has no bot. Resolves as a successful action so the widget's own flow
  // continues the way it would live.
  function invokeAction(actionType, params, variables) {
    tell({ kind: "action", actionType: String(actionType), params: params || null, variables: variables || null });
    return Promise.resolve({ success: true, output: null, error: null, errorCode: null, variables: {} });
  }

  // One preview is the only open copy, so it always wins the claim.
  function claim(key) {
    tell({ kind: "claim", key: String(key) });
    return Promise.resolve(true);
  }

  // A preview plays nothing for the bot, so there is nothing to report; it resolves as accepted.
  function reportYouTubePlayerState() {
    return Promise.resolve(true);
  }

  // An Error carries its stack, which the editor maps back to a file and line. A plain message has none.
  function report(error) {
    var isError = error && typeof error === "object" && typeof error.message === "string";
    tell({ kind: "error", message: isError ? error.message : String(error), stack: isError ? String(error.stack || "") : "" });
  }

  // One console argument as the author would read it: text as is, an object as JSON, an Error as its stack.
  function show(value) {
    if (typeof value === "string") return value;
    if (value instanceof Error) return String(value.stack || value.message);
    if (value === undefined) return "undefined";
    if (typeof value === "function" || typeof value === "symbol" || typeof value === "bigint") return String(value);
    try { return JSON.stringify(value); } catch (_) { return String(value); }
  }

  // Every console level also goes to the editor's Console panel; the browser console still gets it too.
  ["log", "info", "warn", "error", "debug"].forEach(function (level) {
    var original = window.console && window.console[level];
    if (typeof original !== "function") return;
    window.console[level] = function () {
      var parts = Array.prototype.slice.call(arguments).map(show);
      tell({ kind: "console", level: level, text: parts.join(" "), stack: String(new Error().stack || ""), at: Date.now() });
      return original.apply(window.console, arguments);
    };
  });

  window.addEventListener("error", function (e) { report((e && e.error) || (e && e.message) || "script error"); });
  window.addEventListener("unhandledrejection", function (e) { report((e && e.reason) || "unhandled rejection"); });

  window.addEventListener("message", function (ev) {
    var m = ev.data;
    if (!m) return;
    if (m.__nnzFire) emit(m.__nnzFire.type, m.__nnzFire.data == null ? {} : m.__nnzFire.data);
    else if (m.__nnzSettings) applySettings(m.__nnzSettings);
  });

  var api = {
    on: on,
    off: off,
    onAny: onAny,
    onSettings: onSettings,
    reportError: report,
    reportYouTubePlayerState: reportYouTubePlayerState,
    actions: { invoke: invokeAction, claim: claim },
    get settings() { return currentSettings; },
  };
  window.NomNomz = api;
})();
