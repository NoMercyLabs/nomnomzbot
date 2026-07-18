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
/// Serves the OBS-control BRIDGE page — the control-only browser source the streamer pastes into OBS at
/// the exact URL <c>GetBridgeSetupAsync</c> hands out (<c>/obs-bridge?token={BridgeToken}</c>,
/// obs-control.md §4/§7). The page renders a 1×1 invisible surface and carries NO secrets: the
/// <c>?token=</c> is the channel's <c>BridgeToken</c> and only gates the <c>OBSRelayHub</c> (<c>/hubs/obs</c>)
/// connection — never a dashboard JWT. Over that relay it receives <c>ExecuteObsRequest(commandId,
/// payloadJson)</c> pushes when it is the elected leader and:
/// <list type="bullet">
/// <item>OBS leg — for <c>{kind:"request"|"batch"}</c> payloads it opens/maintains a local OBS-WebSocket v5
/// connection (<c>ws://127.0.0.1:4455</c>; Hello → Identify, mirroring <c>DirectObsTransport</c>'s
/// <c>base64(sha256(...))</c> auth-hash when the local OBS demands a password — otherwise passwordless),
/// runs the request/batch, calls <c>AckCommand(commandId, ok, responseDataJson, error)</c>, and forwards
/// subscribed OBS events via <c>ForwardObsEvent(eventType, eventDataJson)</c>.</item>
/// <item>VTS leg (same page, one relay — vtube-studio.md D1) — for <c>{kind:"vts_request"}</c> payloads it
/// talks to local VTube Studio (<c>ws://localhost:8001</c>) using the VTS API envelope (mirroring
/// <c>DirectVtsTransport</c>: <c>apiName</c>/<c>apiVersion</c>/<c>requestID</c>/<c>messageType</c>/<c>data</c>),
/// authenticating itself as a VTS plugin (its plugin token is cached in <c>localStorage</c>, a local token,
/// never a NoMercy secret), acks with the response <c>data</c>, and forwards subscribed VTS events via
/// <c>ForwardVtsEvent(eventType, payloadJson)</c>.</item>
/// </list>
/// The SignalR connection is a hand-rolled JSON-protocol client (record-separator framing, handshake,
/// keep-alive ping, reconnect with backoff), mirroring <see cref="OverlayHostController"/>. All debug text is
/// inserted as text nodes only (never markup), so no relayed payload can inject script into the browser
/// source. Served anonymously; the token gates the hub, not this page.
/// </summary>
[ApiController]
[Route("obs-bridge")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ObsBridgeHostController : ControllerBase
{
    private const string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>NomNomzBot OBS Bridge</title>
        <style>
          /* Control-only surface — renders nothing visible (1x1, transparent). */
          html, body { margin: 0; padding: 0; width: 1px; height: 1px; overflow: hidden; background: transparent; }
          #status { position: fixed; left: -9999px; top: -9999px; }
        </style>
        </head>
        <body>
        <div id="status" aria-hidden="true"></div>
        <script>
        (function () {
          "use strict";
          var RS = String.fromCharCode(30); // SignalR JSON hub protocol record separator (0x1e)
          var params = new URLSearchParams(location.search);
          var token = params.get("token");
          if (!token) { console.error("[obs-bridge] missing ?token="); return; }

          // Invisible debug line — text nodes ONLY (never markup), so no relayed payload can inject script.
          var statusEl = document.getElementById("status");
          function status(text) { if (statusEl) statusEl.textContent = String(text); }

          // ── OBS leg: a local OBS-WebSocket v5 connection (mirrors DirectObsTransport) ──────────────
          var obs = (function () {
            var URL = "ws://127.0.0.1:4455";
            // This page carries NO secret (obs-control.md §4: the password is never in the URL). The local OBS a
            // streamer configures for the bridge is normally passwordless; if a password is required the actual
            // OBSRelayHub contract does not yet deliver one, so obsPassword stays null and Identify goes out
            // unauthenticated. computeAuth mirrors the v5 hash so a future relay-delivered password just works.
            var obsPassword = null;

            var sock = null, ready = false, connecting = false;
            var pending = {}; // requestId -> callback(dResponse)
            var queue = [];   // commands awaiting a live, identified socket

            function b64(buffer) {
              var bytes = new Uint8Array(buffer), s = "";
              for (var i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
              return btoa(s);
            }
            // v5 handshake: base64(sha256(base64(sha256(password + salt)) + challenge)) — binary SHA-256.
            function computeAuth(password, salt, challenge) {
              var enc = new TextEncoder();
              return crypto.subtle.digest("SHA-256", enc.encode(password + salt)).then(function (secretBuf) {
                var secret = b64(secretBuf);
                return crypto.subtle.digest("SHA-256", enc.encode(secret + challenge)).then(b64);
              });
            }

            function connect() {
              if (connecting || ready) return;
              connecting = true;
              try { sock = new WebSocket(URL); }
              catch (e) { connecting = false; failAll("OBS socket could not open"); return; }

              sock.onmessage = function (evt) {
                var msg; try { msg = JSON.parse(evt.data); } catch (_) { return; }
                switch (msg.op) {
                  case 0: onHello(msg.d || {}); break;           // Hello -> Identify
                  case 2: onIdentified(); break;                  // Identified -> ready
                  case 7: case 9: onResponse(msg.d || {}); break; // request / batch response
                  case 5: onEvent(msg.d || {}); break;            // subscribed event
                  default: break;
                }
              };
              sock.onclose = function () { reset("OBS connection closed"); };
              sock.onerror = function () { try { sock.close(); } catch (_) {} };
            }

            function onHello(d) {
              var send = function (authentication) {
                sock.send(JSON.stringify({ op: 1, d: { rpcVersion: 1, authentication: authentication } }));
              };
              if (d.authentication) {
                if (!obsPassword) {
                  console.error("[obs-bridge] local OBS requires a password but the bridge has none — set OBS WebSocket to no auth.");
                  send(null); // OBS will reject; onclose fails the queued commands gracefully.
                  return;
                }
                computeAuth(obsPassword, d.authentication.salt, d.authentication.challenge)
                  .then(send)
                  .catch(function () { send(null); });
              } else {
                send(null); // passwordless — the common bridge setup.
              }
            }

            function onIdentified() {
              ready = true; connecting = false;
              status("obs ready");
              var q = queue; queue = [];
              q.forEach(function (c) { run(c.commandId, c.payload); });
            }

            function onResponse(d) {
              var id = d.requestId;
              if (id && pending[id]) { var cb = pending[id]; delete pending[id]; cb(d); }
            }

            function onEvent(d) {
              var type = d.eventType || "";
              if (!type) return;
              var dataJson = d.eventData !== undefined ? JSON.stringify(d.eventData) : "{}";
              relay.forwardObsEvent(type, dataJson);
            }

            function reset(reason) {
              ready = false; connecting = false; sock = null;
              var ids = Object.keys(pending);
              ids.forEach(function (id) { var cb = pending[id]; delete pending[id]; cb(null, reason); });
            }
            function failAll(reason) {
              var q = queue; queue = [];
              q.forEach(function (c) { relay.ack(c.commandId, false, null, reason); });
            }

            // Send one op-6 request / op-8 batch, correlated by requestId = commandId, then ack.
            function run(commandId, payload) {
              var frame, isBatch = payload.kind === "batch";
              if (isBatch) {
                frame = { op: 8, d: {
                  requestId: commandId,
                  haltOnFailure: !!payload.haltOnFailure,
                  executionType: (typeof payload.executionType === "number" ? payload.executionType : -1),
                  requests: (payload.requests || []).map(function (r) {
                    return { requestType: r.requestType, requestData: r.requestData };
                  })
                } };
              } else {
                frame = { op: 6, d: {
                  requestType: payload.requestType,
                  requestId: commandId,
                  requestData: payload.requestData
                } };
              }

              pending[commandId] = function (d, reason) {
                if (!d) { relay.ack(commandId, false, null, reason || "OBS connection dropped"); return; }
                if (isBatch) {
                  var results = d.results || [];
                  var allOk = results.every(function (r) { return r.requestStatus && r.requestStatus.result; });
                  relay.ack(commandId, allOk, JSON.stringify({ results: results }),
                    allOk ? null : "One or more batch requests failed.");
                } else {
                  var st = d.requestStatus || {};
                  var ok = !!st.result;
                  var data = d.responseData !== undefined ? JSON.stringify(d.responseData) : "{}";
                  relay.ack(commandId, ok, data, ok ? null : (st.comment || ("OBS request failed (code " + st.code + ").")));
                }
              };
              try { sock.send(JSON.stringify(frame)); }
              catch (e) { delete pending[commandId]; relay.ack(commandId, false, null, "OBS send failed"); }
            }

            return {
              execute: function (commandId, payload) {
                if (ready && sock && sock.readyState === WebSocket.OPEN) { run(commandId, payload); return; }
                queue.push({ commandId: commandId, payload: payload });
                connect();
              }
            };
          })();

          // ── VTS leg (same page, same relay — vtube-studio.md D1): local VTube Studio plugin ─────────
          var vts = (function () {
            var URL = "ws://localhost:8001";
            var TOKEN_KEY = "nnz-vts-plugin-token";
            var PLUGIN_NAME = "NomNomzBot", PLUGIN_DEVELOPER = "NoMercy Labs";

            var sock = null, authed = false, connecting = false, requestedToken = false;
            var pending = {}; // requestID -> callback(doc)
            var queue = [];   // {commandId, requestType, dataObj} awaiting an authenticated session

            function envelope(requestType, requestId, dataObj) {
              return JSON.stringify({
                apiName: "VTubeStudioPublicAPI",
                apiVersion: "1.0",
                requestID: requestId,
                messageType: requestType,
                data: dataObj || {}
              });
            }
            function send(requestType, requestId, dataObj, cb) {
              pending[requestId] = cb;
              try { sock.send(envelope(requestType, requestId, dataObj)); }
              catch (e) { delete pending[requestId]; if (cb) cb(null, "VTS send failed"); }
            }

            function connect() {
              if (connecting || authed) return;
              connecting = true;
              try { sock = new WebSocket(URL); }
              catch (e) { connecting = false; failAll("VTS socket could not open"); return; }

              sock.onopen = function () { authenticate(); };
              sock.onmessage = function (evt) {
                var doc; try { doc = JSON.parse(evt.data); } catch (_) { return; }
                var id = doc.requestID, type = doc.messageType || "";
                if (id && pending[id]) { var cb = pending[id]; delete pending[id]; cb(doc); return; }
                // Unsolicited *Event frame from a subscription -> forward to the trigger surface.
                if (type.slice(-5) === "Event") relay.forwardVtsEvent(type, JSON.stringify(doc.data || {}));
              };
              sock.onclose = function () { reset("VTS connection closed"); };
              sock.onerror = function () { try { sock.close(); } catch (_) {} };
            }

            function authenticate() {
              var stored = null;
              try { stored = localStorage.getItem(TOKEN_KEY); } catch (_) {}
              if (stored) { authWithToken(stored); }
              else { requestToken(); }
            }

            function requestToken() {
              requestedToken = true;
              send("AuthenticationTokenRequest", "vts-token", { pluginName: PLUGIN_NAME, pluginDeveloper: PLUGIN_DEVELOPER },
                function (doc, err) {
                  if (!doc) { reset(err || "VTS token request failed"); return; }
                  var t = doc.data && doc.data.authenticationToken;
                  if (!t) { reset("VTS did not grant a plugin token"); return; }
                  try { localStorage.setItem(TOKEN_KEY, t); } catch (_) {}
                  authWithToken(t);
                });
            }

            function authWithToken(pluginToken) {
              send("AuthenticationRequest", "vts-auth",
                { pluginName: PLUGIN_NAME, pluginDeveloper: PLUGIN_DEVELOPER, authenticationToken: pluginToken },
                function (doc, err) {
                  if (!doc) { reset(err || "VTS auth failed"); return; }
                  var ok = doc.data && doc.data.authenticated === true;
                  if (ok) { authed = true; connecting = false; status("vts ready"); flush(); return; }
                  // Stored token was rejected — drop it and request a fresh one exactly once.
                  try { localStorage.removeItem(TOKEN_KEY); } catch (_) {}
                  if (!requestedToken) requestToken();
                  else reset("VTS rejected the plugin token — re-approve in VTube Studio.");
                });
            }

            function flush() {
              var q = queue; queue = [];
              q.forEach(function (c) { run(c.commandId, c.requestType, c.dataObj); });
            }

            function reset(reason) {
              authed = false; connecting = false; requestedToken = false; sock = null;
              var ids = Object.keys(pending);
              ids.forEach(function (id) { var cb = pending[id]; delete pending[id]; if (cb) cb(null, reason); });
              failAll(reason);
            }
            function failAll(reason) {
              var q = queue; queue = [];
              q.forEach(function (c) { relay.ack(c.commandId, false, null, reason); });
            }

            function run(commandId, requestType, dataObj) {
              send(requestType, commandId, dataObj, function (doc, err) {
                if (!doc) { relay.ack(commandId, false, null, err || "VTS connection dropped"); return; }
                if (doc.messageType === "APIError") {
                  var m = (doc.data && doc.data.message) || "VTS rejected the request.";
                  relay.ack(commandId, false, null, m);
                } else {
                  relay.ack(commandId, true, JSON.stringify(doc.data || {}), null);
                }
              });
            }

            return {
              execute: function (commandId, payload) {
                var dataObj = {};
                // The relay stringifies the request data (BridgeVtsTransport): payload.data is a JSON string.
                try { dataObj = typeof payload.data === "string" ? JSON.parse(payload.data || "{}") : (payload.data || {}); }
                catch (_) { dataObj = {}; }
                if (authed && sock && sock.readyState === WebSocket.OPEN) { run(commandId, payload.requestType, dataObj); return; }
                queue.push({ commandId: commandId, requestType: payload.requestType, dataObj: dataObj });
                connect();
              }
            };
          })();

          // ── SignalR JSON-protocol relay client (hand-rolled; mirrors the overlay host) ─────────────
          var relay = (function () {
            var ws = null, backoffMs = 1000, handshaken = false;

            function url() {
              var proto = location.protocol === "https:" ? "wss://" : "ws://";
              return proto + location.host + "/hubs/obs?token=" + encodeURIComponent(token);
            }
            function invoke(target, args) {
              if (ws && ws.readyState === WebSocket.OPEN)
                ws.send(JSON.stringify({ type: 1, target: target, arguments: args }) + RS);
            }

            function connect() {
              ws = new WebSocket(url());
              handshaken = false;
              ws.onopen = function () { ws.send(JSON.stringify({ protocol: "json", version: 1 }) + RS); };
              ws.onmessage = function (evt) {
                String(evt.data).split(RS).forEach(function (segment) {
                  if (!segment) return;
                  var msg; try { msg = JSON.parse(segment); } catch (_) { return; }
                  if (!handshaken) {
                    handshaken = true;
                    if (msg.error) { console.error("[obs-bridge] handshake rejected:", msg.error); ws.close(); return; }
                    backoffMs = 1000;
                    console.log("[obs-bridge] relay connected");
                    return;
                  }
                  if (msg.type === 1) dispatch(msg.target, msg.arguments || []);
                });
              };
              ws.onclose = function () {
                console.warn("[obs-bridge] relay disconnected - reconnecting in " + backoffMs + "ms");
                setTimeout(connect, backoffMs);
                backoffMs = Math.min(backoffMs * 2, 30000);
              };
              ws.onerror = function () { try { ws.close(); } catch (_) {} };
            }

            function dispatch(target, args) {
              if (target !== "ExecuteObsRequest") return;
              var commandId = args[0], payloadJson = args[1];
              var payload; try { payload = JSON.parse(payloadJson); }
              catch (e) { ack(commandId, false, null, "Malformed bridge payload."); return; }
              if (payload && payload.kind === "vts_request") vts.execute(commandId, payload);
              else obs.execute(commandId, payload || {}); // "request" | "batch"
            }

            function ack(commandId, ok, dataJson, error) {
              invoke("AckCommand", [commandId, ok, dataJson || null, error || null]);
            }

            // Keep-alive: the server evicts silent clients (~30s) - ping well under it.
            setInterval(function () {
              if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ type: 6 }) + RS);
            }, 15000);

            return {
              start: connect,
              ack: ack,
              forwardObsEvent: function (eventType, eventDataJson) { invoke("ForwardObsEvent", [eventType, eventDataJson]); },
              forwardVtsEvent: function (eventType, payloadJson) { invoke("ForwardVtsEvent", [eventType, payloadJson]); }
            };
          })();

          relay.start();
        })();
        </script>
        </body>
        </html>
        """;

    /// <summary>The control-only bridge browser source. <c>token</c> gates the relay hub, not this page.</summary>
    [HttpGet]
    public IActionResult Get() => Content(Html, "text/html");
}
