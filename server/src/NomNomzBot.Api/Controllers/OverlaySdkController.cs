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
using Microsoft.AspNetCore.RateLimiting;

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
[EnableRateLimiting(NomNomzBot.Api.RateLimiting.RateLimitPolicyNames.Anonymous)]
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
          // Set once the FIRST connection ever completes its handshake. A later reconnect (bot restart,
          // redeploy, network blip) means this page may be running stale JS/config — instead of quietly
          // resuming with whatever was loaded at page-open, reload so the widget always ends up on the
          // current code + settings without anyone touching OBS. The widget never has to be manually
          // refreshed; it keeps retrying and self-heals the moment the bot is back.
          var hadPriorConnection = false;

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
          // The long-lived overlay token never rides on the WebSocket URL: it is exchanged, over a plain
          // fetch() (which CAN carry a header, unlike the WS upgrade OBS browser sources use), for a
          // short-lived single-use ticket — only THAT appears in the hub query string. A fresh ticket is
          // fetched before every connect (including reconnects), since a ticket burns on first use.
          function wsUrl(ticket) {
            var proto = location.protocol === "https:" ? "wss://" : "ws://";
            return proto + location.host + "/hubs/overlay?ticket=" + encodeURIComponent(ticket || "");
          }

          function fetchTicket() {
            return fetch("/overlay/ticket", { method: "POST", headers: { "X-Overlay-Token": token || "" } })
              .then(function (r) { if (!r.ok) throw new Error("ticket request failed: " + r.status); return r.json(); })
              .then(function (body) { return body.ticket; });
          }

          function connect() {
            if (!token) { console.error("[widget] missing token — cannot connect to the overlay hub"); return; }
            fetchTicket().then(openSocket).catch(function (e) {
              console.error("[widget] could not obtain an overlay ticket:", e);
              setTimeout(connect, backoffMs);
              backoffMs = Math.min(backoffMs * 2, 30000);
            });
          }

          function openSocket(ticket) {
            ws = new WebSocket(wsUrl(ticket));
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
                  if (hadPriorConnection) { location.reload(); return; }
                  hadPriorConnection = true;
                  // JoinWidgetResponse.initialState IS the saved Widget.Settings bag — deliver it so the
                  // first paint lands even without a following WidgetSettingsChanged push.
                  if (msg.result.initialState) applySettings(msg.result.initialState);
                }
              });
            };

            ws.onclose = function () { setTimeout(connect, backoffMs); backoffMs = Math.min(backoffMs * 2, 30000); };
            ws.onerror = function () { try { ws.close(); } catch (_) {} };
          }

          // ── Shared audio bus: every widget page gets working sound playback for free, without a
          // single line of widget code. Sound clips and self-host/BYOK TTS both arrive as PlaySound
          // (a server-rendered audio URL); client_edge TTS arrives as TtsSpeak (browser speechSynthesis,
          // no audio bytes). Widgets that want to react visually still get the raw events via on(...).
          var soundHandles = {}; // handle -> HTMLAudioElement, for StopSound(handle) / StopSound(all)

          function playSound(payload) {
            var el = document.createElement("audio");
            el.src = payload.playbackUrl;
            el.volume = Math.max(0, Math.min(100, Number(payload.volume) || 100)) / 100;
            if (payload.handle) soundHandles[payload.handle] = el;
            el.addEventListener("ended", function () { if (payload.handle) delete soundHandles[payload.handle]; });
            el.play().catch(function (e) { report("audio playback blocked: " + ((e && e.message) || e)); });
          }

          // TTS plays one utterance at a time — overlapping voices are unintelligible, and a busy chat can
          // dispatch several within a second.
          var ttsQueue = [];
          // Set by a dashboard-pushed "pause" control; playNextTts refuses to advance while true, and a
          // fresh utterance arriving mid-pause is queued but not auto-started (see speakTts below).
          var ttsPaused = false;
          function playNextTts() {
            if (ttsPaused) return;
            var el = ttsQueue[0];
            if (!el) return;
            var advance = function () {
              ttsQueue.shift();
              playNextTts();
            };
            el.addEventListener("ended", advance);
            el.addEventListener("error", advance);
            el.play().catch(function (e) {
              report("tts playback blocked: " + ((e && e.message) || e));
              advance();
            });
          }

          // Dashboard-driven live queue controls (skip/clear/pause/resume) — pushed as a "tts_queue_control"
          // WidgetEvent from TtsConfigController's playback/* endpoints. The server never sees what is
          // queued or playing (that state lives only here), so these mutate ttsQueue/ttsPaused directly.
          function ttsSkip() {
            var el = ttsQueue[0];
            if (el) { el.pause(); ttsQueue.shift(); }
            playNextTts();
          }
          function ttsClear() {
            var el = ttsQueue[0];
            if (el) el.pause();
            ttsQueue = [];
          }
          function ttsPause() {
            ttsPaused = true;
            var el = ttsQueue[0];
            if (el) el.pause();
          }
          function ttsResume() {
            ttsPaused = false;
            playNextTts();
          }
          function ttsQueueControl(data) {
            switch ((data || {}).action) {
              case "skip": ttsSkip(); break;
              case "clear": ttsClear(); break;
              case "pause": ttsPause(); break;
              case "resume": ttsResume(); break;
              default: break;
            }
          }

          function stopSound(payload) {
            if (payload.all) {
              Object.keys(soundHandles).forEach(function (h) { soundHandles[h].pause(); delete soundHandles[h]; });
              return;
            }
            var el = payload.handle && soundHandles[payload.handle];
            if (el) { el.pause(); delete soundHandles[payload.handle]; }
          }

          // Cached browser voice list — some browsers populate it async via voiceschanged, so a lookup right
          // after page load can see an empty array; re-cache whenever the event fires and re-read on every call.
          var cachedVoices = null;
          function browserVoices() {
            var synth = window.speechSynthesis;
            var list = synth.getVoices();
            if (list && list.length) cachedVoices = list;
            return cachedVoices || list || [];
          }
          if (window.speechSynthesis) {
            window.speechSynthesis.onvoiceschanged = function () { browserVoices(); };
          }

          // tts.md §6.2: on client_edge the SDK MUST resolve utter.voice/utter.lang from the server-resolved
          // voice — never the browser default. Match the browser's own voice list by id/name first (exact,
          // case-insensitive), then by locale; when nothing matches, utter.lang still steers the browser's
          // own default voice for that language instead of whatever it would otherwise have picked.
          function pickBrowserVoice(voiceId, locale) {
            var list = browserVoices();
            if (!list.length) return null;
            var idLower = (voiceId || "").toLowerCase();
            var byId = null;
            for (var i = 0; i < list.length; i++) {
              var v = list[i];
              var name = (v.voiceURI || v.name || "").toLowerCase();
              if (name === idLower) { byId = v; break; }
            }
            if (byId) return byId;
            if (locale) {
              var localeLower = locale.toLowerCase();
              for (var j = 0; j < list.length; j++) {
                if ((list[j].lang || "").toLowerCase() === localeLower) return list[j];
              }
              var lang = localeLower.split("-")[0];
              for (var k = 0; k < list.length; k++) {
                if ((list[k].lang || "").toLowerCase().indexOf(lang) === 0) return list[k];
              }
            }
            return null;
          }

          function speakTts(payload) {
            // Server-synthesised audio is the NORMAL path and must win. OBS captures a browser source's
            // media elements but NOT speechSynthesis, which plays straight out of the system audio device —
            // so speaking through the browser voice is audible to the streamer and silent on stream.
            if (payload.audioUrl) {
              var tts = document.createElement("audio");
              tts.src = payload.audioUrl;
              var vol = (payload.options || {}).volume;
              tts.volume = vol == null ? 1 : Math.max(0, Math.min(1, Number(vol)));
              ttsQueue.push(tts);
              if (ttsQueue.length === 1) playNextTts();
              return;
            }
            var synth = window.speechSynthesis;
            if (!synth) { report("speechSynthesis unavailable — cannot render client_edge TTS"); return; }
            var utter = new SpeechSynthesisUtterance(payload.text);
            var opts = payload.options || {};
            if (opts.rate != null) utter.rate = opts.rate;
            if (opts.pitch != null) utter.pitch = opts.pitch;
            if (opts.volume != null) utter.volume = opts.volume;
            var matched = pickBrowserVoice(payload.voiceId, payload.locale);
            if (matched) {
              utter.voice = matched;
              utter.lang = matched.lang;
            } else if (payload.locale) {
              utter.lang = payload.locale;
            }
            synth.speak(utter);
          }

          // eventType -> the SDK's own playback for it. WidgetEvent is subscription-routed per widget (only
          // a widget that DECLARES the event type is ever sent one), so autoplaying here for a recognised
          // type is exactly as scoped as the raw PlaySound/TtsSpeak hub targets below -- it just also covers
          // the delivery path server code actually uses for self-host/BYOK TTS (TtsUtteranceDispatchedEvent
          // routes through WidgetAlertDispatch -> WidgetEvent, never through the raw TtsSpeak target).
          var AUTOPLAY = {
            tts_speak: speakTts,
            play_sound: playSound,
            stop_sound: stopSound,
            tts_queue_control: ttsQueueControl,
          };

          function dispatch(target, args) {
            switch (target) {
              case "WidgetEvent": {
                var e = args[0] || {};
                var data = e.data || {};
                var autoplay = AUTOPLAY[e.eventType];
                if (autoplay) autoplay(data);
                emit(e.eventType, data);
                break;
              }
              case "WidgetSettingsChanged": applySettings((args[0] || {}).settings || {}); break;
              case "WidgetReload": location.reload(); break;
              case "Event": { var oe = args[0] || {}; emit(oe.type, oe.payload); break; }
              // Raw hub targets: unused by current server code (WidgetNotifier only ever sends WidgetEvent),
              // kept so a future broadcaster-wide push (bypassing per-widget subscription) still autoplays.
              case "PlaySound": { var ps = args[0] || {}; playSound(ps); emit("play_sound", ps); break; }
              case "StopSound": { var ss = args[0] || {}; stopSound(ss); emit("stop_sound", ss); break; }
              case "TtsSpeak": { var ts = args[0] || {}; speakTts(ts); emit("tts_speak", ts); break; }
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
