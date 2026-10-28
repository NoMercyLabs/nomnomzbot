// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Jint;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S2 defect B: the served bridge script runs in Jint against fake sockets. The bridge must open its local OBS
/// socket as soon as the relay hands it the channel settings (not on the first command), identify with the
/// channel's saved event mask, forward events, and redial OBS after it closes.
/// </summary>
public sealed class ObsBridgeScriptBehaviorTests
{
    private const string Prelude = """
        var RS_ = String.fromCharCode(30);
        var __sockets = [];
        var __timers = [];
        function WebSocket(url) { this.url = url; this.readyState = 0; this.sent = []; __sockets.push(this); }
        WebSocket.OPEN = 1;
        WebSocket.prototype.send = function (d) { this.sent.push(String(d)); };
        WebSocket.prototype.close = function () { this.readyState = 3; if (this.onclose) this.onclose({ code: 1006 }); };
        var location = { search: "?token=t", protocol: "http:", host: "bot.test" };
        function URLSearchParams(q) { this.q = q; }
        URLSearchParams.prototype.get = function (k) { return k === "token" ? "t" : null; };
        var document = { getElementById: function () { return { textContent: "" }; } };
        var console = { log: function () {}, warn: function () {}, error: function () {} };
        function setTimeout(fn, ms) { __timers.push({ fn: fn, ms: ms }); return __timers.length; }
        function setInterval() { return 0; }
        var localStorage = { getItem: function () { return null; }, setItem: function () {}, removeItem: function () {} };
        function __open(s) { s.readyState = 1; if (s.onopen) s.onopen(); }
        function __relayPush(obj) { __sockets[0].onmessage({ data: JSON.stringify(obj) + RS_ }); }
        function __obsFrame(s, obj) { s.onmessage({ data: JSON.stringify(obj) }); }
        function __obsSockets() { return __sockets.filter(function (s) { return s.url.indexOf("ws://127.0.0.1:") === 0; }); }
        function __runTimers() { var t = __timers; __timers = []; t.forEach(function (x) { x.fn(); }); }
        """;

    private static Engine StartBridgeWithSettings(int port, int mask)
    {
        ContentResult page = (ContentResult)new ObsBridgeHostController().Get();
        string script = Regex
            .Match(page.Content!, "<script nonce=\"[^\"]+\">(?<js>[\\s\\S]*?)</script>")
            .Groups["js"]
            .Value;
        Engine engine = new();
        engine.Execute(Prelude);
        engine.Execute(script);
        engine.Execute(
            "__open(__sockets[0]); __relayPush({});"
                + $"__relayPush({{ type: 1, target: 'SetObsCredentials', arguments: [null, {port}, {mask}] }});"
        );
        return engine;
    }

    [Fact]
    public void The_bridge_dials_local_obs_on_load_and_identifies_with_the_channel_mask()
    {
        Engine engine = StartBridgeWithSettings(4460, 4 | 64);

        engine
            .Evaluate("__obsSockets().map(function (s) { return s.url; }).join(',')")
            .AsString()
            .Should()
            .Be("ws://127.0.0.1:4460", "the OBS leg opens before any command arrives");

        engine.Execute(
            "var o = __obsSockets()[0]; __open(o); __obsFrame(o, { op: 0, d: { rpcVersion: 1 } });"
        );
        engine
            .Evaluate("JSON.parse(__obsSockets()[0].sent[0]).d.eventSubscriptions")
            .AsNumber()
            .Should()
            .Be(4 | 64, "Identify subscribes with the channel's saved event mask");
    }

    [Fact]
    public void An_obs_event_is_forwarded_to_the_relay_without_any_command()
    {
        Engine engine = StartBridgeWithSettings(4455, 0xFFF);
        engine.Execute(
            "var o = __obsSockets()[0]; __open(o); __obsFrame(o, { op: 0, d: { rpcVersion: 1 } });"
                + "__obsFrame(o, { op: 2, d: { negotiatedRpcVersion: 1 } });"
                + "__obsFrame(o, { op: 5, d: { eventType: 'CurrentProgramSceneChanged', eventData: { sceneName: 'Game' } } });"
        );

        engine
            .Evaluate(
                "__sockets[0].sent.filter(function (f) { return f.indexOf('ForwardObsEvent') >= 0; }).length"
            )
            .AsNumber()
            .Should()
            .Be(1);
    }

    [Fact]
    public void The_bridge_redials_obs_after_the_obs_socket_closes()
    {
        Engine engine = StartBridgeWithSettings(4455, 0xFFF);
        engine.Execute(
            "var o = __obsSockets()[0]; __open(o); __obsFrame(o, { op: 0, d: { rpcVersion: 1 } });"
                + "__obsFrame(o, { op: 2, d: { negotiatedRpcVersion: 1 } }); o.close(); __runTimers();"
        );

        engine
            .Evaluate("__obsSockets().length")
            .AsNumber()
            .Should()
            .Be(2, "a closed OBS socket is redialed so events keep flowing after OBS restarts");
    }
}
