// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Jint;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Runs the served overlay SDK in Jint behind a fake browser: a WebSocket that records what the widget sends, a
/// ticket fetch that always succeeds, and no-op timers and DOM. The SDK is connected and hand-shaken, so a test
/// delivers hub messages exactly as the overlay hub frames them and asserts what the widget code receives.
/// </summary>
internal sealed class OverlaySdkRuntime
{
    private const char RecordSeparator = (char)30;

    private const string Browser = """
        var window = this;
        window.addEventListener = function () {};
        var console = { error: function () {}, warn: function () {}, log: function () {} };
        var location = { search: "?token=overlay-token&widgetId=widget-1", protocol: "https:", host: "bot.test", reload: function () {} };
        function URLSearchParams(query) {
          var values = {};
          query.replace(/^\?/, "").split("&").forEach(function (pair) {
            if (!pair) return;
            var parts = pair.split("=");
            values[decodeURIComponent(parts[0])] = decodeURIComponent(parts[1] || "");
          });
          this.get = function (key) { return key in values ? values[key] : null; };
        }
        var setInterval = function () { return 0; };
        var setTimeout = function () { return 0; };
        var __sockets = [];
        function WebSocket(url) { this.url = url; this.readyState = 1; this.sent = []; __sockets.push(this); }
        WebSocket.OPEN = 1;
        WebSocket.prototype.send = function (data) { this.sent.push(String(data)); };
        WebSocket.prototype.close = function () {};
        var fetch = function () {
          return Promise.resolve({ status: 200, ok: true, json: function () { return Promise.resolve({ ticket: "ticket-1" }); } });
        };
        var __plays = 0;
        var __spoken = 0;
        function __element() {
          return { style: {}, addEventListener: function () {}, play: function () { __plays++; return Promise.resolve(); }, pause: function () {} };
        }
        function SpeechSynthesisUtterance(text) { this.text = text; }
        var speechSynthesis = { getVoices: function () { return []; }, speak: function () { __spoken++; } };
        window.speechSynthesis = speechSynthesis;
        var document = {
          getElementById: function () { return null; },
          createElement: __element,
          body: { appendChild: function () {} },
          documentElement: { appendChild: function () {} }
        };
        """;

    private static readonly JsonSerializerOptions HubJson = new(JsonSerializerDefaults.Web);

    private readonly Engine _engine;

    private OverlaySdkRuntime(Engine engine) => _engine = engine;

    /// <summary>The SDK loaded, its socket open, and the SignalR handshake answered.</summary>
    public static OverlaySdkRuntime Connected()
    {
        Engine engine = new();
        engine.Execute(Browser);
        engine.Execute(ServedSdk());

        OverlaySdkRuntime runtime = new(engine);
        runtime.Evaluate("__sockets[0].onopen()");
        runtime.Receive("{}");
        return runtime;
    }

    /// <summary>Delivers one hub invocation, framed as the overlay hub sends it.</summary>
    public void Deliver(string target, object argument)
    {
        string message = JsonSerializer.Serialize(
            new
            {
                type = 1,
                target,
                arguments = new[] { argument },
            },
            HubJson
        );
        Receive(message);
    }

    /// <summary>How many audio elements the page has started playing.</summary>
    public int AudioPlays => (int)_engine.Evaluate("__plays").AsNumber();

    /// <summary>How many utterances the page handed to the browser's own speech voice.</summary>
    public int BrowserSpeeches => (int)_engine.Evaluate("__spoken").AsNumber();

    /// <summary>Every frame the page sent over its socket.</summary>
    public string SentFrames => _engine.Evaluate("__sockets[0].sent.join('|')").ToString();

    public void Evaluate(string script) => _engine.Evaluate(script);

    /// <summary>The script's value as text, the way JavaScript prints it.</summary>
    public string Text(string script) => _engine.Evaluate(script).ToString();

    private void Receive(string frame)
    {
        _engine.SetValue("__frame", frame + RecordSeparator);
        _engine.Evaluate("__sockets[0].onmessage({ data: __frame })");
    }

    private static string ServedSdk()
    {
        OverlaySdkController controller = new()
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        ContentResult result = (ContentResult)controller.Get();
        return result.Content!;
    }
}
