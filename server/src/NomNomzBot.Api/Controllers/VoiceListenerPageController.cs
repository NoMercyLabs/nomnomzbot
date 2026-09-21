// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// Serves the voice-listener page (voice-triggers feature): a plain, no-build-step HTML+JS page meant to be
/// opened directly in a REAL Chrome browser tab — never added as an OBS browser source. OBS's CEF browser
/// source has the Web Speech API's <c>SpeechRecognition</c> disabled entirely (a confirmed, longstanding CEF
/// limitation), so this page cannot run inside OBS; it is a normal browser tab the streamer leaves open in the
/// background while streaming. It runs Chrome's free <c>webkitSpeechRecognition</c> in continuous mode,
/// auto-restarting on <c>onend</c>/<c>onerror</c> (Chrome silently stops continuous recognition periodically —
/// expected behavior, not a bug), and POSTs each transcript chunk to <c>VoiceTriggerReportController</c> using
/// the SAME <c>X-Overlay-Token</c> header the SDK uses — the token is embedded server-side from the URL's
/// <c>?token=</c> query, never typed by the streamer. Served anonymously; the token itself is the credential,
/// same trust model as <c>OverlaySdkController</c>. Every FINAL recognition chunk (never interim results — no
/// per-word network spam) is reported once, and <c>VoiceTriggerService.ReportAsync</c> does double duty: it
/// checks the chunk against defined trigger words AND persists it as a transcript segment against the
/// channel's current live stream, so the Analytics per-stream detail page can show the full spoken transcript.
/// </summary>
[ApiController]
[Route("voice-listener")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(NomNomzBot.Api.RateLimiting.RateLimitPolicyNames.Anonymous)]
public sealed class VoiceListenerPageController : ControllerBase
{
    // The listener is deliberately a single-file page, so authorize its one inline script with a fresh nonce
    // instead of weakening script-src with 'unsafe-inline'. SecurityHeadersMiddleware exempts this exact route
    // from the dashboard policy so the two policies do not intersect and reject the nonce-bearing script.
    private static string ContentSecurityPolicy(string nonce) =>
        "default-src 'none'; "
        + $"script-src 'self' 'nonce-{nonce}'; "
        + "style-src 'self' 'unsafe-inline'; "
        + "connect-src 'self'; "
        + "base-uri 'none'; object-src 'none'; form-action 'none'";

    private const string PageTemplate = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>NomNomzBot — Voice Listener</title>
        <style>
          body { font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; background:#0c0c12; color:#eee; margin:0; padding:24px; }
          .card { max-width:640px; margin:0 auto; background:#16161f; border:1px solid #2a2a38; border-radius:12px; padding:24px; }
          h1 { font-size:20px; margin:0 0 4px; }
          .sub { color:#9a9aab; font-size:13px; margin:0 0 20px; }
          .status { display:inline-flex; align-items:center; gap:8px; padding:6px 12px; border-radius:999px; font-size:13px; font-weight:600; }
          .status.listening { background:#123a1e; color:#4ade80; }
          .status.stopped { background:#3a1212; color:#f87171; }
          .dot { width:8px; height:8px; border-radius:50%; background:currentColor; }
          .transcript { margin-top:16px; min-height:64px; background:#0c0c12; border:1px solid #2a2a38; border-radius:8px; padding:12px; font-size:14px; color:#c7c7d6; white-space:pre-wrap; word-break:break-word; }
          .warn { margin-top:16px; padding:12px; border-radius:8px; background:#3a2a12; color:#facc15; font-size:13px; display:none; }
          .warn.show { display:block; }
          .hint { margin-top:20px; font-size:12px; color:#6f6f80; line-height:1.5; }
        </style>
        </head>
        <body>
        <div class="card">
          <h1>Voice Listener</h1>
          <p class="sub">Keep this REAL Chrome tab open in the background while you stream. Do not add it as an OBS browser source.</p>
          <div id="status" class="status stopped"><span class="dot"></span><span id="statusText">Starting…</span></div>
          <div id="transcript" class="transcript">(waiting for speech)</div>
          <div id="warn" class="warn"></div>
          <p class="hint">This page listens for your defined trigger words using Chrome's free, built-in speech recognition and reports a hit to NomNomzBot. Nothing you say is sent anywhere except a short match check against your own trigger words. Closing this tab stops voice triggers from firing until you reopen it.</p>
        </div>
        <script nonce="__NONCE__">
        (function () {
          "use strict";
          var token = __TOKEN_JSON__;
          var statusEl = document.getElementById("status");
          var statusText = document.getElementById("statusText");
          var transcriptEl = document.getElementById("transcript");
          var warnEl = document.getElementById("warn");

          function setListening(on) {
            statusEl.className = "status " + (on ? "listening" : "stopped");
            statusText.textContent = on ? "Listening" : "Stopped — restarting…";
          }

          function warn(message) {
            warnEl.textContent = message;
            warnEl.className = "warn show";
          }

          var SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
          if (!SpeechRecognition) {
            warn("This browser has no speech recognition support. Open this page in a real Chrome browser tab.");
            setListening(false);
            return;
          }
          if (!token) {
            warn("Missing listener token — open this page from the Voice Triggers screen in the dashboard.");
            setListening(false);
            return;
          }

          function report(transcript) {
            fetch("/overlay/voice-trigger/report", {
              method: "POST",
              headers: { "Content-Type": "application/json", "X-Overlay-Token": token },
              body: JSON.stringify({ transcript: transcript })
            }).catch(function () { /* network hiccup — the next chunk will retry naturally */ });
          }

          var recognition = null;
          var restarting = false;

          function start() {
            recognition = new SpeechRecognition();
            recognition.continuous = true;
            recognition.interimResults = true;
            recognition.lang = "en-US";

            recognition.onstart = function () { setListening(true); };

            recognition.onresult = function (event) {
              var text = "";
              for (var i = event.resultIndex; i < event.results.length; i++) {
                text += event.results[i][0].transcript;
                if (event.results[i].isFinal) report(event.results[i][0].transcript);
              }
              if (text) transcriptEl.textContent = text;
            };

            recognition.onerror = function (event) {
              if (event.error === "not-allowed" || event.error === "service-not-allowed")
                warn("Microphone access was blocked. Allow microphone access for this page and reload.");
            };

            // Chrome silently ends continuous recognition periodically — this is expected, not a failure.
            // Restart it so the streamer never has to babysit this tab.
            recognition.onend = function () {
              setListening(false);
              if (restarting) return;
              restarting = true;
              setTimeout(function () { restarting = false; start(); }, 250);
            };

            try { recognition.start(); } catch (e) { setTimeout(start, 1000); }
          }

          start();
        })();
        </script>
        </body>
        </html>
        """;

    /// <summary>Serves the listener page. The token travels only in the URL query the dashboard's copy-link button builds.</summary>
    [HttpGet]
    public IActionResult Get([FromQuery] string? token)
    {
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        string tokenJson = token is null
            ? "null"
            : System.Text.Json.JsonSerializer.Serialize(token);
        string html = PageTemplate
            .Replace("__NONCE__", nonce)
            .Replace("__TOKEN_JSON__", tokenJson);
        Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy(nonce);
        return Content(html, "text/html; charset=utf-8");
    }
}
