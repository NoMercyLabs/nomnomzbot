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
/// group when a <c>widgetId</c> is given, and RENDERS the built-in widget surfaces the server already
/// pushes (<c>WidgetAlertHandlers</c>): transient alerts (follow/subscription/resub/gift/cheer/raid), the
/// standing now-playing pill, and the hype-train meter. Event payload text is inserted as text nodes only
/// (never markup), so a chatter's display name cannot inject script into the browser source. Widget
/// settings (<c>accentColor</c>, <c>durationMs</c>) apply on join and live via <c>WidgetSettingsChanged</c>;
/// unknown keys are ignored. It is the runtime shell compiled widget bundles will later load into
/// (widgets-overlays.md). The page itself carries no secrets and is served anonymously; the token only
/// gates the hub.
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
          :root { --accent: #9146ff; --fg: #ffffff; }
          html, body { margin: 0; padding: 0; background: transparent; overflow: hidden; }
          body { font: 16px system-ui, sans-serif; color: var(--fg); }
          #audio-unlock {
            display: none; position: fixed; top: 8px; left: 8px; padding: 6px 12px;
            font-size: 13px; color: #fff; background: rgba(0,0,0,.7);
            border-radius: 6px; cursor: pointer; user-select: none;
          }
          /* Transient alert card — centered upper third, slide+fade. */
          #alert-box {
            position: fixed; top: 12%; left: 50%; transform: translate(-50%, -16px);
            padding: 18px 34px; border-radius: 12px; text-align: center;
            background: rgba(10,10,14,.85); border: 2px solid var(--accent);
            box-shadow: 0 4px 24px rgba(0,0,0,.5);
            opacity: 0; transition: opacity .3s ease, transform .3s ease;
            pointer-events: none; max-width: 70vw;
          }
          #alert-box.show { opacity: 1; transform: translate(-50%, 0); }
          #alert-box .alert-title { font-size: 26px; font-weight: 700; color: var(--accent); }
          #alert-box .alert-detail { font-size: 17px; margin-top: 6px; opacity: .9; }
          /* Standing now-playing pill — bottom left. */
          #now-playing {
            display: none; position: fixed; left: 16px; bottom: 16px; align-items: center;
            padding: 8px 16px; border-radius: 999px; background: rgba(10,10,14,.85);
            border: 1px solid var(--accent); font-size: 15px; max-width: 46vw;
            white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
          }
          #now-playing .note { color: var(--accent); margin-right: 8px; }
          /* Standing hype-train meter — top right. */
          #hype-train {
            display: none; position: fixed; top: 16px; right: 16px; width: 260px;
            padding: 10px 14px; border-radius: 10px; background: rgba(10,10,14,.85);
            border: 1px solid var(--accent);
          }
          #hype-train .ht-label { font-size: 14px; font-weight: 700; color: var(--accent); }
          #hype-train .ht-bar { height: 8px; margin-top: 6px; border-radius: 4px; background: rgba(255,255,255,.15); }
          #hype-train .ht-fill { height: 100%; width: 0; border-radius: 4px; background: var(--accent); transition: width .4s ease; }
        </style>
        </head>
        <body>
        <div id="audio-unlock">&#128266; Click to enable audio</div>
        <div id="alert-box"><div class="alert-title"></div><div class="alert-detail"></div></div>
        <div id="now-playing"><span class="note">&#9835;</span><span class="np-track"></span></div>
        <div id="hype-train"><div class="ht-label"></div><div class="ht-bar"><div class="ht-fill"></div></div></div>
        <script>
        (function () {
          "use strict";
          var RS = String.fromCharCode(30); // SignalR JSON hub protocol record separator (0x1e)
          var params = new URLSearchParams(location.search);
          var token = params.get("token");
          var widgetId = params.get("widgetId");
          if (!token) { console.error("[overlay] missing ?token="); return; }

          // ── Widget appearance settings (applied on join + WidgetSettingsChanged) ──
          var settings = { durationMs: 6000 };
          function applySettings(s) {
            if (!s || typeof s !== "object") return;
            if (typeof s.accentColor === "string" && s.accentColor)
              document.documentElement.style.setProperty("--accent", s.accentColor);
            var d = Number(s.durationMs);
            if (isFinite(d) && d >= 1000 && d <= 60000) settings.durationMs = d;
          }

          // ── Transient alert queue — one card at a time, text nodes only (no markup injection) ──
          var alertBox = document.getElementById("alert-box");
          var alertTitle = alertBox.querySelector(".alert-title");
          var alertDetail = alertBox.querySelector(".alert-detail");
          var alertQueue = [];
          var alertShowing = false;

          function alertText(type, d) {
            d = d || {};
            switch (type) {
              case "follow": return { title: d.user + " just followed!", detail: "" };
              case "subscription": return { title: d.user + " just subscribed!", detail: tierText(d.tier) };
              case "resub": return { title: d.user + " resubscribed!", detail: (d.months || 0) + " months " + tierText(d.tier) };
              case "gift": return { title: d.user + " gifted " + (d.amount || 1) + " sub" + (d.amount === 1 ? "" : "s") + "!", detail: tierText(d.tier) };
              case "cheer": return { title: d.user + " cheered " + (d.amount || 0) + " bits!", detail: "" };
              case "raid": return { title: d.user + " is raiding!", detail: (d.viewers || 0) + " viewers incoming" };
              default: return null;
            }
          }

          function tierText(tier) {
            if (tier === "2000") return "Tier 2";
            if (tier === "3000") return "Tier 3";
            return tier ? "Tier 1" : "";
          }

          function enqueueAlert(type, data) {
            var text = alertText(type, data);
            if (!text) return;
            alertQueue.push(text);
            if (!alertShowing) showNextAlert();
          }

          function showNextAlert() {
            var next = alertQueue.shift();
            if (!next) { alertShowing = false; return; }
            alertShowing = true;
            alertTitle.textContent = next.title;
            alertDetail.textContent = next.detail;
            alertDetail.style.display = next.detail ? "block" : "none";
            alertBox.classList.add("show");
            setTimeout(function () {
              alertBox.classList.remove("show");
              setTimeout(showNextAlert, 400); // let the fade-out finish before the next card
            }, settings.durationMs);
          }

          // ── Standing surfaces: now-playing pill + hype-train meter ──
          var nowPlaying = document.getElementById("now-playing");
          var nowPlayingTrack = nowPlaying.querySelector(".np-track");
          function renderNowPlaying(d) {
            d = d || {};
            if (d.isPlaying && d.track) {
              nowPlayingTrack.textContent = d.track;
              nowPlaying.style.display = "flex";
            } else {
              nowPlaying.style.display = "none";
            }
          }

          var hypeTrain = document.getElementById("hype-train");
          var hypeLabel = hypeTrain.querySelector(".ht-label");
          var hypeFill = hypeTrain.querySelector(".ht-fill");
          function renderHypeTrain(type, d) {
            d = d || {};
            if (type === "hype_train_end") {
              hypeLabel.textContent = "HYPE TRAIN complete - LV " + (d.level || 1);
              hypeFill.style.width = "100%";
              setTimeout(function () { hypeTrain.style.display = "none"; }, 5000);
              return;
            }
            var pct = d.goal > 0 ? Math.min(100, Math.round((d.progress / d.goal) * 100)) : 0;
            hypeLabel.textContent = "HYPE TRAIN LV " + (d.level || 1);
            hypeFill.style.width = pct + "%";
            hypeTrain.style.display = "block";
          }

          function renderWidgetEvent(evt) {
            if (!evt || !evt.eventType) return;
            var type = evt.eventType;
            var data = evt.data || {};
            if (type === "now_playing") { renderNowPlaying(data); return; }
            if (type.indexOf("hype_train") === 0) { renderHypeTrain(type, data); return; }
            enqueueAlert(type, data);
          }

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
                  applySettings(msg.result.settings);
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
              case "WidgetEvent": renderWidgetEvent(args[0]); break;
              case "WidgetSettingsChanged": applySettings((args[0] || {}).settings); break;
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
