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
/// Serves the overlay SDK (<c>/overlay/sdk.js</c>) — the tiny global (<c>window.NomNomz</c>) every widget builds on.
/// A widget runs inside a null-origin sandboxed iframe (it cannot open its own hub connection), so the SDK is a
/// postMessage bridge to the overlay host page, which owns the single SignalR connection and forwards events. The
/// SDK exposes <c>on</c>/<c>off</c>/<c>onAny</c>/<c>onSettings</c>/<c>settings</c>/<c>reportError</c>, and reports
/// uncaught widget errors to the host (audit B5 runtime health). Served anonymously; carries no secrets.
/// </summary>
[ApiController]
[Route("overlay")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class OverlaySdkController : ControllerBase
{
    private const string Sdk = """
        /* NomNomzBot Overlay SDK — window.NomNomz. Bridges the sandboxed widget iframe to the overlay host page
           over postMessage; the host owns the SignalR connection and forwards events + settings here. */
        (function () {
          "use strict";
          var host = window.parent;
          var handlers = {};        // eventType -> [fn]
          var anyHandlers = [];     // fn(eventType, data)
          var settingsHandlers = []; // fn(settings)
          var currentSettings = {};

          function report(message) {
            try { host.postMessage({ nnz: 1, kind: "error", message: String(message) }, "*"); } catch (_) {}
          }

          function run(fn, a, b) { try { fn(a, b); } catch (e) { report((e && e.message) || e); } }

          function on(type, fn) {
            if (typeof fn === "function") (handlers[type] = handlers[type] || []).push(fn);
            return api;
          }
          function off(type, fn) {
            var list = handlers[type];
            if (list) handlers[type] = list.filter(function (h) { return h !== fn; });
            return api;
          }
          function onAny(fn) { if (typeof fn === "function") anyHandlers.push(fn); return api; }
          function onSettings(fn) {
            if (typeof fn === "function") {
              settingsHandlers.push(fn);
              if (Object.keys(currentSettings).length) run(fn, currentSettings);
            }
            return api;
          }

          function emit(type, data) {
            (handlers[type] || []).forEach(function (fn) { run(fn, data, type); });
            anyHandlers.forEach(function (fn) { run(fn, type, data); });
          }

          window.addEventListener("message", function (evt) {
            if (evt.source !== host) return;           // only trust the host frame
            var m = evt.data;
            if (!m || m.nnz !== 1) return;
            if (m.kind === "event") emit(m.eventType, m.data || {});
            else if (m.kind === "settings") {
              currentSettings = m.settings || {};
              settingsHandlers.forEach(function (fn) { run(fn, currentSettings); });
            }
          });

          // Surface uncaught widget errors to the host (audit B5 runtime health).
          window.addEventListener("error", function (e) { report((e && e.message) || "script error"); });
          window.addEventListener("unhandledrejection", function (e) {
            report((e && e.reason && e.reason.message) || "unhandled rejection");
          });

          var api = {
            on: on,
            off: off,
            onAny: onAny,
            onSettings: onSettings,
            reportError: report,
            get settings() { return currentSettings; },
          };
          window.NomNomz = api;

          // Announce readiness so the host sends our current settings + starts forwarding events.
          try { host.postMessage({ nnz: 1, kind: "ready" }, "*"); } catch (_) {}
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
