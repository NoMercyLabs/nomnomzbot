// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// Serves the overlay SDK (<c>/overlay/sdk.js</c>) — the global (<c>window.NomNomz</c>) every widget builds on.
/// A widget is a real standalone SPA (its own page, not an iframe), so the SDK owns the widget's OWN SignalR
/// connection to the overlay hub: it reads the server-injected config (<c>window.WIDGET_SETTINGS</c>), joins its
/// widget group, and dispatches the channel's subscription-matched events + live settings changes to the widget.
/// The public surface is unchanged from the postMessage era (<c>on</c>/<c>off</c>/<c>onAny</c>/<c>onSettings</c>/
/// <c>settings</c>/<c>reportError</c>) so widget code ports without edits — only the transport changed. Served
/// anonymously; the token that gates the hub rides in on the page URL, never in this script.
/// </summary>
[ApiController]
[Route("overlay")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class OverlaySdkController : ControllerBase
{
    private const string Sdk = """
        /* NomNomzBot Overlay SDK — window.NomNomz. The widget is a standalone SPA; this SDK opens the widget's own
           SignalR connection to /hubs/overlay, reads the server-injected window.WIDGET_SETTINGS, joins its widget
           group, and delivers the subscription-matched WidgetEvent feed + live WidgetSettingsChanged to the widget.
           API: on / off / onAny / onSettings / settings / reportError (unchanged from the postMessage era). */
        (function () {
          "use strict";
          var RS = String.fromCharCode(30); // SignalR JSON hub-protocol record separator (0x1e)
          var params = new URLSearchParams(location.search);
          var token = window.WIDGET_TOKEN || params.get("token");
          var widgetId = window.WIDGET_ID || params.get("widgetId");

          var handlers = {};         // eventType -> [fn]
          var anyHandlers = [];      // fn(eventType, data)
          var settingsHandlers = []; // fn(settings)
          // Seed settings from the server-injected config so onSettings fires with the real config on first paint,
          // before the hub even connects — a widget renders configured, never with a flash of defaults.
          var currentSettings = (window.WIDGET_SETTINGS && typeof window.WIDGET_SETTINGS === "object")
            ? window.WIDGET_SETTINGS : {};

          var ws = null;
          var backoffMs = 1000;

          function run(fn, a, b) { try { fn(a, b); } catch (e) { report((e && e.message) || e); } }

          function report(message) {
            console.error("[widget] error:", message);
            try {
              if (widgetId && ws && ws.readyState === WebSocket.OPEN)
                ws.send(JSON.stringify({ type: 1, target: "ReportRuntimeError", arguments: [widgetId, String(message)] }) + RS);
            } catch (_) {}
          }

          function on(type, fn) { if (typeof fn === "function") (handlers[type] = handlers[type] || []).push(fn); return api; }
          function off(type, fn) { var l = handlers[type]; if (l) handlers[type] = l.filter(function (h) { return h !== fn; }); return api; }
          function onAny(fn) { if (typeof fn === "function") anyHandlers.push(fn); return api; }
          function onSettings(fn) {
            if (typeof fn === "function") { settingsHandlers.push(fn); run(fn, currentSettings); }
            return api;
          }
          function emit(type, data) {
            (handlers[type] || []).forEach(function (fn) { run(fn, data, type); });
            anyHandlers.forEach(function (fn) { run(fn, type, data); });
          }
          function applySettings(s) {
            if (!s || typeof s !== "object") return;
            currentSettings = s;
            settingsHandlers.forEach(function (fn) { run(fn, currentSettings); });
          }

          // ── The widget's own SignalR (JSON protocol) connection to the overlay hub ──
          function wsUrl() {
            var proto = location.protocol === "https:" ? "wss://" : "ws://";
            return proto + location.host + "/hubs/overlay?token=" + encodeURIComponent(token || "");
          }

          function connect() {
            if (!token) { console.error("[widget] missing token — cannot connect to the overlay hub"); return; }
            ws = new WebSocket(wsUrl());
            var handshaken = false;

            ws.onopen = function () { ws.send(JSON.stringify({ protocol: "json", version: 1 }) + RS); };

            ws.onmessage = function (evt) {
              String(evt.data).split(RS).forEach(function (segment) {
                if (!segment) return;
                var msg; try { msg = JSON.parse(segment); } catch (_) { return; }

                if (!handshaken) {
                  handshaken = true;
                  if (msg.error) { console.error("[widget] handshake rejected:", msg.error); ws.close(); return; }
                  backoffMs = 1000;
                  if (widgetId)
                    ws.send(JSON.stringify({ type: 1, invocationId: "join", target: "JoinWidget", arguments: [widgetId] }) + RS);
                  return;
                }

                if (msg.type === 1) dispatch(msg.target, msg.arguments || []);
                else if (msg.type === 3 && msg.invocationId === "join" && msg.result) {
                  // JoinWidgetResponse.initialState IS the saved Widget.Settings bag — deliver it so a reconnect or a
                  // settings change made while offline lands even without a following WidgetSettingsChanged push.
                  if (msg.result.initialState) applySettings(msg.result.initialState);
                }
              });
            };

            ws.onclose = function () { setTimeout(connect, backoffMs); backoffMs = Math.min(backoffMs * 2, 30000); };
            ws.onerror = function () { try { ws.close(); } catch (_) {} };
          }

          function dispatch(target, args) {
            switch (target) {
              case "WidgetEvent": { var e = args[0] || {}; emit(e.eventType, e.data || {}); break; }
              case "WidgetSettingsChanged": applySettings((args[0] || {}).settings || {}); break;
              case "WidgetReload": location.reload(); break;
              default: break;
            }
          }

          // Keep-alive: the hub evicts silent clients (~30s); ping well under it.
          setInterval(function () {
            if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ type: 6 }) + RS);
          }, 15000);

          // Surface uncaught widget errors to the server (runtime health).
          window.addEventListener("error", function (e) { report((e && e.message) || "script error"); });
          window.addEventListener("unhandledrejection", function (e) { report((e && e.reason && e.reason.message) || "unhandled rejection"); });

          var api = {
            on: on,
            off: off,
            onAny: onAny,
            onSettings: onSettings,
            reportError: report,
            get settings() { return currentSettings; },
          };
          window.NomNomz = api;

          connect();
        })();
        """;

    /// <summary>The overlay SDK script. Long-cacheable — the content only changes with a bot upgrade.</summary>
    [HttpGet("sdk.js")]
    public IActionResult Get()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(Sdk, "application/javascript; charset=utf-8");
    }
}
