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
/// unknown keys are ignored. It also MOUNTS the authored widget's compiled bundle (the compile-on-save output,
/// fetched from <c>/api/v1/overlay/bundle/{widgetId}</c>) in a null-origin sandboxed iframe, re-fetched fresh on
/// the <c>WidgetReload</c> full reload so a save hot-swaps the overlay (widgets-overlays.md). The page itself
/// carries no secrets and is served anonymously; the token only gates the hub.
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
          /* ── Chat overlay — ported from the owner's twitch-chat-overlay widget ── */
          /* Bottom-anchored column, newest at bottom, older lines clip off the top; ~100-line DOM cap. */
          #chat {
            position: fixed; left: 0; top: 0; bottom: 0; width: min(440px, 42vw);
            display: flex; flex-direction: column; justify-content: flex-end;
            gap: 12px; padding: 16px 12px 20px 16px; overflow: hidden; pointer-events: none;
          }
          /* Each message: banner + overlapping avatar + bubble, with a staggered slide-in entrance. */
          .chat-msg {
            display: flex; flex-direction: column; opacity: 1;
            transform: translateX(120%);
            transition: transform .5s ease-in-out, opacity .5s ease-in-out;
          }
          .chat-msg.active { transform: translateX(0); }
          .chat-head { position: relative; padding-top: 12px; z-index: 2; }
          .chat-shine {
            position: relative; overflow: hidden; border-radius: 8px;
            transform: translateX(120%); transition: transform .5s ease-out;
          }
          .chat-msg.active .chat-shine { transform: translateX(0); }
          /* Banner background is a gradient derived from the chatter's colour (relative-colour syntax, as in the reference). */
          .chat-banner {
            position: relative; display: flex; align-items: center; gap: 4px;
            height: 32px; padding: 0 48px 0 8px; border-radius: 8px; color: #fff;
            background-color: var(--fallback, var(--accent));
            background-image: linear-gradient(45deg, var(--c300), var(--c500) 23%, var(--c700));
          }
          .chat-banner.on-light { color: #000; }
          .chat-banner.no-avatar { padding-right: 12px; }
          .chat-badge { width: 20px; height: 20px; flex: none; }
          .chat-name { font-weight: 700; font-size: 1.125rem; line-height: 1; }
          .chat-pron { font-size: .8rem; font-weight: 600; font-family: ui-monospace, monospace; opacity: .85; white-space: nowrap; }
          .chat-shine::after {
            content: ""; position: absolute; inset: 0; pointer-events: none; z-index: 1;
            transform: translateX(100%);
            background: linear-gradient(65deg, rgba(255,255,255,0) 0%, rgba(255,255,255,0) 35%, rgba(255,255,255,.2) 50%, rgba(128,186,232,0) 65%, rgba(128,186,232,0) 100%);
          }
          .chat-msg.active .chat-shine::after { animation: chat-shine 12s forwards; }
          @keyframes chat-shine { 1% { transform: translateX(100%); } 15%, 100% { transform: translateX(-100%); } }
          .chat-avatar {
            position: absolute; right: -4px; top: -8px; width: 56px; height: 56px;
            border-radius: 50%; overflow: hidden; z-index: 3; background: #171717; border: 1px solid #171717;
            transform: scale(0); transition: transform .3s ease-out; transition-delay: .3s;
          }
          .chat-msg.active .chat-avatar { transform: scale(1); }
          .chat-avatar img { width: 100%; height: 100%; object-fit: cover; }
          .chat-bubble {
            position: relative; margin-top: -12px; margin-right: 16px; padding: 20px 12px 12px;
            border-radius: 8px; background: rgba(10,10,14,.92); border: 1px solid rgba(255,255,255,.06);
            overflow: clip; z-index: 1; transform: scaleY(0); transform-origin: top;
            transition: transform .4s ease-out; transition-delay: .6s;
          }
          .chat-msg.active .chat-bubble { transform: scaleY(1); }
          .chat-content {
            font-size: 1.1rem; line-height: 1.6; font-weight: 500; color: #fff; word-break: break-word;
            opacity: 0; transition: opacity .3s ease-out; transition-delay: .9s;
          }
          .chat-msg.active .chat-content { opacity: 1; }
          .chat-content.emote-only { display: flex; align-items: center; gap: 4px; }
          .chat-emote { height: 28px; width: auto; vertical-align: middle; margin: 0 2px; }
          .chat-content.emote-only .chat-emote, .chat-emote-big { height: 72px; }
          .chat-mention { font-weight: 700; }
          .chat-cheer-bits { font-weight: 700; margin-left: 2px; }
          .chat-link { color: var(--accent); text-decoration: underline; }
        </style>
        </head>
        <body>
        <div id="audio-unlock">&#128266; Click to enable audio</div>
        <div id="alert-box"><div class="alert-title"></div><div class="alert-detail"></div></div>
        <div id="now-playing"><span class="note">&#9835;</span><span class="np-track"></span></div>
        <div id="hype-train"><div class="ht-label"></div><div class="ht-bar"><div class="ht-fill"></div></div></div>
        <div id="chat" aria-live="polite"></div>
        <script>
        (function () {
          "use strict";
          var RS = String.fromCharCode(30); // SignalR JSON hub protocol record separator (0x1e)
          var params = new URLSearchParams(location.search);
          var token = params.get("token");
          var widgetId = params.get("widgetId");
          if (!token) { console.error("[overlay] missing ?token="); return; }

          // Tri-state: does the joined widget have an authored bundle? null = still fetching, true = mounted,
          // false = no bundle (404 / fetch error) or no widget joined. The shell's built-in surfaces (chat, alerts,
          // now-playing, hype) render ONLY when this is false — a bundleless fallback. While pending (null) or when a
          // bundle is present (true) the widget's own compiled bundle is the SOLE renderer (dev-platform.md), so the
          // built-ins stay dark: a first-party bundle already receives its subscribed events (incl. "ChatMessage")
          // over the targeted WidgetEvent feed, and re-rendering them here would double every line on the overlay.
          var bundlePresent = null;

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

          // ── Chat — ported from the owner's twitch-chat-overlay widget ──────
          // Renders a DashboardChatMessageDto (camelCase): colour-derived name banner with badges,
          // overlapping avatar, dark bubble with resolved emotes/mentions/cheermotes/links. Newest at
          // bottom, ~100-line cap. All chatter-controlled strings go in via text nodes only (never markup);
          // emote/badge/avatar images go in img.src; colours are hex-validated before use.
          var CHAT_CAP = 100;
          var chatBox = document.getElementById("chat");
          var HEX = /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/;

          function safeColor(c) {
            return (typeof c === "string" && HEX.test(c.trim())) ? c.trim() : null;
          }

          function luminance(hex) {
            var h = hex.replace("#", "");
            if (h.length === 3) h = h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
            var r = parseInt(h.slice(0, 2), 16), g = parseInt(h.slice(2, 4), 16), b = parseInt(h.slice(4, 6), 16);
            return 0.299 * r + 0.587 * g + 0.114 * b;
          }

          function firstUrl(urls, keys) {
            if (!urls) return null;
            for (var i = 0; i < keys.length; i++) if (urls[keys[i]]) return urls[keys[i]];
            return null;
          }

          function isEmoteOnly(m) {
            var f = m.fragments;
            if (!f || !f.length) return false;
            for (var i = 0; i < f.length; i++) {
              if (f[i].type === "emote") continue;
              if (f[i].type === "text" && (!f[i].text || !f[i].text.trim())) continue;
              return false;
            }
            return true;
          }

          function appendImage(parent, url, alt, cls) {
            var img = document.createElement("img");
            img.className = cls;
            img.src = url;
            img.alt = alt || "";
            parent.appendChild(img);
          }

          function appendEmote(parent, fr, big) {
            var url = big
              ? firstUrl(fr.emote && fr.emote.urls, ["3", "2", "1"])
              : firstUrl(fr.emote && fr.emote.urls, ["2", "1", "3"]);
            if (!url) { parent.appendChild(document.createTextNode(fr.text || "")); return; }
            appendImage(parent, url, fr.text, big ? "chat-emote chat-emote-big" : "chat-emote");
          }

          function appendCheermote(parent, fr) {
            var url = firstUrl(fr.cheermote && fr.cheermote.urls, ["2", "1", "3"]);
            if (!url) { parent.appendChild(document.createTextNode(fr.text || "")); return; }
            appendImage(parent, url, fr.text, "chat-emote");
            var bits = document.createElement("span");
            bits.className = "chat-cheer-bits";
            var col = safeColor(fr.cheermote.colorHex);
            if (col) bits.style.color = col;
            bits.textContent = String(fr.cheermote.bits || 0);
            parent.appendChild(bits);
          }

          function appendMention(parent, fr) {
            var span = document.createElement("span");
            span.className = "chat-mention";
            var col = safeColor(fr.mention && fr.mention.color);
            if (col) span.style.color = col;
            span.textContent = fr.text || "";
            parent.appendChild(span);
          }

          function appendLink(parent, fr) {
            var url = fr.linkUrl || fr.text || "";
            if (/^https?:\/\//i.test(url)) {
              var a = document.createElement("a");
              a.className = "chat-link";
              a.setAttribute("href", url);
              a.setAttribute("target", "_blank");
              a.setAttribute("rel", "noopener noreferrer");
              a.textContent = fr.text || url;
              parent.appendChild(a);
            } else {
              parent.appendChild(document.createTextNode(fr.text || url));
            }
          }

          function renderContent(m, content) {
            var emoteOnly = isEmoteOnly(m);
            if (emoteOnly) content.classList.add("emote-only");
            if (m.fragments && m.fragments.length) {
              m.fragments.forEach(function (fr) {
                switch (fr.type) {
                  case "emote": appendEmote(content, fr, emoteOnly); break;
                  case "cheermote": appendCheermote(content, fr); break;
                  case "mention": appendMention(content, fr); break;
                  case "link": appendLink(content, fr); break;
                  default: content.appendChild(document.createTextNode(fr.text || "")); break;
                }
              });
            } else {
              content.appendChild(document.createTextNode(m.message || ""));
            }
          }

          function accentColor() {
            var a = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim();
            return a || "#9146ff";
          }

          function renderChat(m) {
            if (!m || !chatBox) return;

            var userColor = safeColor(m.color) || accentColor();
            var light = luminance(userColor) > 140;

            var msg = document.createElement("div");
            msg.className = "chat-msg";

            var head = document.createElement("div");
            head.className = "chat-head";

            var shine = document.createElement("div");
            shine.className = "chat-shine";

            var banner = document.createElement("div");
            banner.className = "chat-banner" + (light ? " on-light" : "");
            banner.style.setProperty("--fallback", userColor);
            banner.style.setProperty("--c300", "hsl(from " + userColor + " h calc(s * .30) l)");
            banner.style.setProperty("--c500", "hsl(from " + userColor + " h calc(s * .50) l)");
            banner.style.setProperty("--c700", "hsl(from " + userColor + " h s calc(l * .70))");

            (m.badges || []).forEach(function (b) {
              var url = firstUrl(b.urls, ["2", "1", "4"]);
              if (url) appendImage(banner, url, b.setId, "chat-badge");
            });

            var name = document.createElement("span");
            name.className = "chat-name";
            name.textContent = m.displayName || m.username || "";
            banner.appendChild(name);

            if (m.pronouns) {
              var pron = document.createElement("span");
              pron.className = "chat-pron";
              pron.textContent = "(" + m.pronouns + ")";
              banner.appendChild(pron);
            }

            shine.appendChild(banner);
            head.appendChild(shine);

            if (m.avatarUrl) {
              var av = document.createElement("div");
              av.className = "chat-avatar";
              appendImage(av, m.avatarUrl, "", "");
              head.appendChild(av);
            } else {
              banner.classList.add("no-avatar");
            }

            var bubble = document.createElement("div");
            bubble.className = "chat-bubble";
            var content = document.createElement("div");
            content.className = "chat-content";
            renderContent(m, content);
            bubble.appendChild(content);

            msg.appendChild(head);
            msg.appendChild(bubble);
            chatBox.appendChild(msg);

            // Cap the DOM: drop oldest lines beyond the cap (already clipped off the top, so no visible pop).
            while (chatBox.children.length > CHAT_CAP)
              chatBox.removeChild(chatBox.firstChild);

            // Kick off the staggered banner -> avatar -> bubble -> content entrance on the next frame.
            requestAnimationFrame(function () {
              requestAnimationFrame(function () { msg.classList.add("active"); });
            });
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
                else if (msg.type === 3 && msg.invocationId === "join" && msg.result) {
                  // JoinWidgetResponse serializes as {success,error,initialState}; initialState IS the
                  // saved Widget.Settings bag (accentColor/durationMs at top level) — not a .settings field.
                  widgetSettings = msg.result.initialState || {};
                  applySettings(msg.result.initialState);
                  // Deliver the saved settings to the custom widget now that they're loaded. The iframe's SDK
                  // usually posts "ready" (below) BEFORE this join response arrives, and at that point
                  // widgetSettings was still empty — so without this re-send the widget never receives its
                  // configured settings on first load (it renders with defaults until a later change event).
                  // A no-op if the iframe hasn't mounted yet; the "ready" handler then delivers these instead.
                  postToCustom({ kind: "settings", settings: widgetSettings });
                }
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
              case "WidgetEvent":
                // Always hand the targeted (subscription-matched) event to the widget bundle; render the built-in
                // alert / now-playing / hype surface only when this overlay has no bundle (the fallback case).
                if (bundlePresent === false) renderWidgetEvent(args[0]);
                postToCustom({ kind: "event", eventType: (args[0] || {}).eventType, data: (args[0] || {}).data });
                break;
              case "Event": {
                // The channel-wide feed drives the built-in chat surface, which renders ONLY as a bundleless
                // fallback. A widget with a bundle already receives its subscribed events (including "ChatMessage")
                // over the targeted WidgetEvent feed above, so re-forwarding this channel-wide copy into the iframe
                // would render every message twice — hence it is NOT posted to the widget.
                if (bundlePresent !== false) break;
                var oe = args[0];
                if (oe && oe.type === "ChatMessage") {
                  try { renderChat(JSON.parse(oe.payload)); }
                  catch (e) { console.error("[overlay] bad chat payload", e); }
                }
                break;
              }
              case "WidgetSettingsChanged":
                widgetSettings = (args[0] || {}).settings || {};
                applySettings(widgetSettings);
                postToCustom({ kind: "settings", settings: widgetSettings });
                break;
              default: break;
            }
          }

          // ── Custom-widget injector + SDK bridge ─────────────────────────────
          // Render the widget's AUTHORED bundle (the compile-on-save output) in a sandboxed iframe, with the
          // overlay SDK (window.NomNomz) loaded first. The widget cannot open its own hub connection (null-origin
          // sandbox), so this host owns the SignalR connection and forwards events + settings to the widget over
          // postMessage; the SDK inside dispatches them. Fetched fresh (no-store) so the WidgetReload -> full page
          // reload shows the just-compiled bundle. A 404 means no authored bundle (a built-in/type widget) - the
          // shell above already renders it. Untrusted custom code runs scripts-only, with no access to this page.
          var customFrame = null;
          var widgetSettings = {}; // the full saved Settings bag, forwarded to the widget (accentColor + custom keys)

          function postToCustom(msg) {
            if (customFrame && customFrame.contentWindow) {
              try { msg.nnz = 1; customFrame.contentWindow.postMessage(msg, "*"); } catch (_) {}
            }
          }

          // Report a widget runtime fault to the server (audit B5) - throttled so a widget erroring in a loop
          // cannot flood the hub.
          var lastErrorAt = 0;
          function reportRuntimeError(message) {
            console.error("[overlay] widget runtime error:", message);
            var now = Date.now();
            if (now - lastErrorAt < 5000) return;
            lastErrorAt = now;
            if (widgetId && ws && ws.readyState === WebSocket.OPEN)
              ws.send(JSON.stringify({ type: 1, target: "ReportRuntimeError", arguments: [widgetId, String(message)] }) + RS);
          }

          // Messages FROM the widget iframe: send its settings once it is ready; log any runtime error (audit B5).
          window.addEventListener("message", function (evt) {
            if (!customFrame || evt.source !== customFrame.contentWindow) return;
            var m = evt.data;
            if (!m || m.nnz !== 1) return;
            if (m.kind === "ready") postToCustom({ kind: "settings", settings: widgetSettings });
            else if (m.kind === "error") reportRuntimeError(m.message);
          });

          function mountCustomWidget() {
            // A bare overlay (no ?widgetId=) has no bundle — fall the shell back to its built-in surfaces.
            if (!widgetId) { bundlePresent = false; return; }
            var url = "/api/v1/overlay/bundle/" + encodeURIComponent(widgetId)
              + "?token=" + encodeURIComponent(token);
            fetch(url, { cache: "no-store" }).then(function (r) {
              // 404 / non-OK = a bundleless (built-in-type) widget; light up the built-in surfaces instead.
              if (!r.ok) { bundlePresent = false; return null; }
              var isHtml = (r.headers.get("content-type") || "").indexOf("text/html") === 0;
              var framework = (r.headers.get("X-Widget-Framework") || "").toLowerCase();
              return r.text().then(function (body) { return { body: body, isHtml: isHtml, framework: framework }; });
            }).then(function (res) {
              if (!res) return;
              // The SDK loads first (parser-blocking), so window.NomNomz is defined before the widget's scripts run.
              var sdkTag = '<script src="' + location.origin + '/overlay/sdk.js"><\/script>';
              // A compiled vue widget keeps its `vue` import external and self-mounts via window.Vue. Inject the Vue
              // runtime BEFORE the SDK (and thus before the widget bundle) so window.Vue is defined when it mounts.
              // Same-origin scripts DO load inside the null-origin sandboxed iframe (the SDK already relies on this).
              var vueTag = res.framework === "vue"
                ? '<script src="' + location.origin + '/overlay/vue.js"><\/script>'
                : '';
              var frame = document.createElement("iframe");
              frame.id = "custom-widget";
              frame.setAttribute("sandbox", "allow-scripts");
              frame.style.cssText =
                "position:fixed;inset:0;width:100%;height:100%;border:0;background:transparent;";
              frame.srcdoc = vueTag + sdkTag + (res.isHtml ? res.body
                : '<!doctype html><meta charset="utf-8">'
                  + '<body style="margin:0;background:transparent"><script>' + res.body + '<\/script></body>');
              customFrame = frame;
              document.body.appendChild(frame);
              // The authored bundle is now the sole renderer — the built-in surfaces stay dark from here on.
              bundlePresent = true;
            }).catch(function (e) {
              // Fetch/parse failed — fall back to the shell's built-in surfaces rather than a blank overlay.
              bundlePresent = false;
              console.error("[overlay] custom widget mount failed", e);
            });
          }

          // Keep-alive: the server evicts silent clients (~30s) and server frames do not reset that
          // timer - ping well under it.
          setInterval(function () {
            if (ws && ws.readyState === WebSocket.OPEN)
              ws.send(JSON.stringify({ type: 6 }) + RS);
          }, 15000);

          connect();
          mountCustomWidget();
        })();
        </script>
        </body>
        </html>
        """;

    /// <summary>The browser-source shell. <c>token</c> gates the hub connection, not this page.</summary>
    [HttpGet]
    public IActionResult Get() => Content(Html, "text/html");
}
