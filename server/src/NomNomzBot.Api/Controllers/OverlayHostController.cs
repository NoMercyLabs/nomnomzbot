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
/// Serves the overlay HOST page — the OBS browser source shell at the exact URL the widgets API hands
/// out (<c>/overlay?widgetId={id}&amp;token={overlayToken}</c>). The page connects to <c>/hubs/overlay</c>
/// (the hub validates the per-channel <c>OverlayToken</c> on connect), implements the audio bus
/// (<c>PlaySound</c>/<c>StopSound</c> — walk-in sounds, the play_sound pipeline action), joins its widget
/// group when a <c>widgetId</c> is given, and logs widget events. It is the runtime shell compiled widget
/// bundles will later load into (widgets-overlays.md) — shipped now so sound-bearing responses are audible
/// end-to-end. The page itself carries no secrets and is served anonymously; the token only gates the hub.
/// </summary>
[ApiController]
[Route("overlay")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class OverlayHostController : ControllerBase
{
    private const string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>NomNomzBot Overlay</title>
        <style>
          html, body { margin: 0; padding: 0; background: transparent; overflow: hidden; }
          #audio-unlock {
            display: none; position: fixed; top: 8px; left: 8px; padding: 6px 12px;
            font: 13px system-ui, sans-serif; color: #fff; background: rgba(0,0,0,.7);
            border-radius: 6px; cursor: pointer; user-select: none;
          }
        </style>
        </head>
        <body>
        <div id="audio-unlock">&#128266; Click to enable audio</div>
        <script>
        (function () {
          "use strict";
          var RS = String.fromCharCode(30); // SignalR JSON hub protocol record separator (0x1e)
          var params = new URLSearchParams(location.search);
          var token = params.get("token");
          var widgetId = params.get("widgetId");
          if (!token) { console.error("[overlay] missing ?token="); return; }

          // ── Audio bus ─────────────────────────────────────────────────────
          // Keyed by handle so stop_sound can target a named clip; anonymous plays get unique keys.
          var playing = new Map();
          var anonSeq = 0;
          var unlockEl = document.getElementById("audio-unlock");
          var pendingUnlock = [];

          function playSound(p) {
            var key = p.handle || ("anon-" + (++anonSeq));
            var audio = new Audio(p.playbackUrl);
            audio.volume = Math.min(Math.max((p.volume == null ? 100 : p.volume) / 100, 0), 1);
            audio.addEventListener("ended", function () { playing.delete(key); });
            playing.set(key, audio);
            audio.play().catch(function () {
              // Autoplay blocked (normal browsers; OBS allows it) — queue until a user gesture.
              pendingUnlock.push(audio);
              unlockEl.style.display = "block";
            });
          }

          function stopSound(p) {
            if (p.all) {
              playing.forEach(function (a) { a.pause(); });
              playing.clear();
              return;
            }
            if (p.handle && playing.has(p.handle)) {
              playing.get(p.handle).pause();
              playing.delete(p.handle);
            }
          }

          unlockEl.addEventListener("click", function () {
            pendingUnlock.forEach(function (a) { a.play().catch(function () {}); });
            pendingUnlock = [];
            unlockEl.style.display = "none";
          });

          // ── SignalR JSON-protocol client (hand-rolled, mirrors the dashboard client) ──
          var ws = null;
          var backoffMs = 1000;

          function wsUrl() {
            var proto = location.protocol === "https:" ? "wss://" : "ws://";
            return proto + location.host + "/hubs/overlay?token=" + encodeURIComponent(token);
          }

          function connect() {
            ws = new WebSocket(wsUrl());
            var handshaken = false;

            ws.onopen = function () {
              ws.send(JSON.stringify({ protocol: "json", version: 1 }) + RS);
            };

            ws.onmessage = function (evt) {
              String(evt.data).split(RS).forEach(function (segment) {
                if (!segment) return;
                var msg;
                try { msg = JSON.parse(segment); } catch (_) { return; }

                if (!handshaken) {
                  // First frame is the handshake response: {} on success, {error} on rejection.
                  handshaken = true;
                  if (msg.error) { console.error("[overlay] handshake rejected:", msg.error); ws.close(); return; }
                  backoffMs = 1000;
                  console.log("[overlay] connected");
                  if (widgetId)
                    ws.send(JSON.stringify({ type: 1, invocationId: "join", target: "JoinWidget", arguments: [widgetId] }) + RS);
                  return;
                }

                if (msg.type === 1) dispatch(msg.target, msg.arguments || []);
                else if (msg.type === 3 && msg.invocationId === "join" && msg.result)
                  console.log("[overlay] widget settings:", msg.result.settings || null);
              });
            };

            ws.onclose = function () {
              console.warn("[overlay] disconnected - reconnecting in " + backoffMs + "ms");
              setTimeout(connect, backoffMs);
              backoffMs = Math.min(backoffMs * 2, 30000);
            };
            ws.onerror = function () { try { ws.close(); } catch (_) {} };
          }

          function dispatch(target, args) {
            switch (target) {
              case "PlaySound": playSound(args[0] || {}); break;
              case "StopSound": stopSound(args[0] || {}); break;
              case "WidgetReload": location.reload(); break;
              case "WidgetEvent": console.log("[overlay] widget event:", args[0]); break;
              case "WidgetSettingsChanged": console.log("[overlay] settings changed:", args[0]); break;
              default: break;
            }
          }

          // Keep-alive: the server evicts silent clients (~30s) and server frames do not reset that
          // timer - ping well under it.
          setInterval(function () {
            if (ws && ws.readyState === WebSocket.OPEN)
              ws.send(JSON.stringify({ type: 6 }) + RS);
          }, 15000);

          connect();
        })();
        </script>
        </body>
        </html>
        """;

    /// <summary>The browser-source shell. <c>token</c> gates the hub connection, not this page.</summary>
    [HttpGet]
    public IActionResult Get() => Content(Html, "text/html");
}
