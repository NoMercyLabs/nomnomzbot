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
using FluentAssertions;
using Jint;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// widget-sdk.md §8 — <c>NomNomz.actions.invoke</c>, run for real: the served SDK executes in Jint against a fake
/// browser (a recording WebSocket, a fetch that hands out a ticket). Proves the hub frame it sends, that the hub's
/// completion resolves the widget's promise with the outcome, and that a dropped socket rejects what is in flight.
/// </summary>
public sealed class OverlaySdkActionInvokeTests
{
    private const char RecordSeparator = (char)30;

    // Just enough browser for the SDK to boot and connect. Timers never fire on their own: nothing here waits.
    private const string FakeBrowser = """
        var window = globalThis;
        var console = { error: function () {}, log: function () {}, warn: function () {} };
        var location = { search: "", protocol: "https:", host: "bot.test", reload: function () {} };
        function URLSearchParams() { this.get = function () { return null; }; }
        function setTimeout() { return 0; }
        function setInterval() { return 0; }
        window.addEventListener = function () {};
        var document = { getElementById: function () { return null; } };
        window.WIDGET_ID = "widget-1";
        window.WIDGET_TOKEN = "token-1";
        var sockets = [];
        function WebSocket(url) { this.url = url; this.readyState = 0; this.sent = []; sockets.push(this); }
        WebSocket.OPEN = 1;
        WebSocket.prototype.send = function (frame) { this.sent.push(frame); };
        WebSocket.prototype.close = function () { this.readyState = 3; if (this.onclose) this.onclose(); };
        function fetch() {
          return Promise.resolve({ ok: true, status: 200, json: function () { return Promise.resolve({ ticket: "t-1" }); } });
        }
        var RS = String.fromCharCode(30);
        function connectSocket() {
          var ws = sockets[sockets.length - 1];
          ws.readyState = 1;
          ws.onopen();
          ws.onmessage({ data: "{}" + RS });
          return ws;
        }
        """;

    private static string ServedSdk()
    {
        OverlaySdkController controller = new()
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        return ((ContentResult)controller.Get()).Content!;
    }

    private static Engine BootedSdk()
    {
        Engine engine = new();
        engine.Execute(FakeBrowser);
        engine.Execute(ServedSdk());
        engine.Execute("var ws = connectSocket();");
        return engine;
    }

    [Fact]
    public void Invoke_sends_an_InvokeAction_frame_with_the_widget_id_params_and_text_variables()
    {
        Engine engine = BootedSdk();

        engine.Execute(
            "NomNomz.actions.invoke('tts_synthesize', { text: 'hi', rate: -5 }, { 'redemption.id': 'r-1', count: 3 });"
        );

        string frame = engine.Evaluate("ws.sent[ws.sent.length - 1]").AsString();
        frame.Should().EndWith(RecordSeparator.ToString());
        JsonElement message = JsonDocument.Parse(frame.TrimEnd(RecordSeparator)).RootElement;
        message.GetProperty("type").GetInt32().Should().Be(1);
        message.GetProperty("target").GetString().Should().Be("InvokeAction");
        message.GetProperty("invocationId").GetString().Should().NotBeNullOrEmpty();
        JsonElement arguments = message.GetProperty("arguments");
        arguments[0].GetString().Should().Be("widget-1");
        arguments[1].GetString().Should().Be("tts_synthesize");
        arguments[2].GetProperty("text").GetString().Should().Be("hi");
        arguments[2].GetProperty("rate").GetInt32().Should().Be(-5);
        arguments[3].GetProperty("redemption.id").GetString().Should().Be("r-1");
        arguments[3].GetProperty("count").GetString().Should().Be("3");
    }

    [Fact]
    public void The_hub_completion_resolves_the_promise_with_the_outcome()
    {
        Engine engine = BootedSdk();
        engine.Execute(
            """
            var outcome = null;
            NomNomz.actions.invoke('song_pause').then(function (r) { outcome = r; });
            var sent = JSON.parse(ws.sent[ws.sent.length - 1].slice(0, -1));
            ws.onmessage({ data: JSON.stringify({ type: 3, invocationId: sent.invocationId,
              result: { success: true, output: 'paused', error: null, errorCode: null, variables: { 'tts.durationMs': '900' } } }) + RS });
            """
        );

        engine.Evaluate("outcome.success").AsBoolean().Should().BeTrue();
        engine.Evaluate("outcome.output").AsString().Should().Be("paused");
        engine.Evaluate("outcome.variables['tts.durationMs']").AsString().Should().Be("900");
    }

    [Fact]
    public void A_dropped_socket_rejects_the_call_in_flight()
    {
        Engine engine = BootedSdk();
        engine.Execute(
            """
            var failure = null;
            NomNomz.actions.invoke('song_resume').catch(function (e) { failure = e.message; });
            ws.close();
            """
        );

        engine.Evaluate("failure").AsString().Should().Contain("lost its connection");
    }

    [Fact]
    public void Invoking_before_the_socket_is_open_rejects_instead_of_hanging()
    {
        Engine engine = new();
        engine.Execute(FakeBrowser);
        engine.Execute(ServedSdk());
        engine.Execute(
            """
            var failure = null;
            NomNomz.actions.invoke('song_pause').catch(function (e) { failure = e.message; });
            """
        );

        engine.Evaluate("failure").AsString().Should().Contain("not connected");
    }
}
