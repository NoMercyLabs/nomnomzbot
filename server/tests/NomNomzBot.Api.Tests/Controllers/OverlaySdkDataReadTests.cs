// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Jint;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The SDK reads the widget's data itself: <c>NomNomz.data.*</c> and <c>NomNomz.spotify.playbackToken</c> call
/// the overlay REST endpoints with the SDK's own token in the <c>X-Overlay-Token</c> header (never in the URL),
/// so a widget never holds or parses the token.
/// </summary>
public sealed class OverlaySdkDataReadTests
{
    private const string FakeBrowser = """
        var window = globalThis;
        var console = { error: function () {}, log: function () {}, warn: function () {} };
        var location = { search: "?token=url-token&widgetId=widget-1", protocol: "https:", host: "bot.test", reload: function () {} };
        function URLSearchParams(q) { this.get = function (k) { return k === "token" ? "url-token" : (k === "widgetId" ? "widget-1" : null); }; }
        function setTimeout() { return 0; }
        function setInterval() { return 0; }
        window.addEventListener = function () {};
        var document = { getElementById: function () { return null; } };
        window.WIDGET_NAME = "Now Playing";
        function WebSocket(url) { this.url = url; this.readyState = 0; this.sent = []; }
        WebSocket.OPEN = 1;
        WebSocket.prototype.send = function () {};
        WebSocket.prototype.close = function () {};
        var calls = [];
        var answers = {};
        function reply(status, data) {
          return Promise.resolve({ ok: status >= 200 && status < 300, status: status, json: function () { return Promise.resolve({ data: data }); } });
        }
        function fetch(url, init) {
          calls.push({ url: String(url), token: init && init.headers ? init.headers["X-Overlay-Token"] : null });
          if (String(url) === "/overlay/ticket") return Promise.resolve({ ok: true, status: 200, json: function () { return Promise.resolve({ ticket: "t" }); } });
          var a = answers[String(url)];
          if (a === "network") return Promise.reject(new Error("offline"));
          return reply(a.status, a.data);
        }
        """;

    private static Engine Booted()
    {
        OverlaySdkController controller = new()
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        Engine engine = new();
        engine.Execute(FakeBrowser);
        engine.Execute(((ContentResult)controller.Get()).Content!);
        return engine;
    }

    [Fact]
    public void NowPlaying_reads_the_snapshot_with_the_token_in_a_header_and_not_in_the_url()
    {
        Engine engine = Booted();
        engine.Execute(
            """
            answers["/api/v1/overlay/now-playing"] = { status: 200, data: { track: "Song", isPlaying: true } };
            var got = null;
            NomNomz.data.nowPlaying().then(function (d) { got = d; });
            """
        );

        engine.Evaluate("got.track").AsString().Should().Be("Song");
        engine.Evaluate("got.isPlaying").AsBoolean().Should().BeTrue();
        engine
            .Evaluate("calls[calls.length - 1].url")
            .AsString()
            .Should()
            .Be("/api/v1/overlay/now-playing");
        engine.Evaluate("calls[calls.length - 1].token").AsString().Should().Be("url-token");
    }

    [Fact]
    public void NowPlaying_resolves_null_when_nothing_plays_and_rejects_on_a_failed_request()
    {
        Engine engine = Booted();
        engine.Execute(
            """
            answers["/api/v1/overlay/now-playing"] = { status: 200, data: null };
            var idle = "unset";
            NomNomz.data.nowPlaying().then(function (d) { idle = d; });
            answers["/api/v1/overlay/queue"] = { status: 500, data: null };
            var failure = null;
            NomNomz.data.queue().catch(function (e) { failure = e.message; });
            """
        );

        engine.Evaluate("idle === null").AsBoolean().Should().BeTrue();
        engine.Evaluate("failure").AsString().Should().Contain("500");
    }

    [Fact]
    public void Queue_resolves_the_list_and_Storage_encodes_the_key_and_resolves_null_for_no_value()
    {
        Engine engine = Booted();
        engine.Execute(
            """
            answers["/api/v1/overlay/queue"] = { status: 200, data: [{ trackName: "A" }, { trackName: "B" }] };
            answers["/api/v1/overlay/storage/hot%20potato%2Fholder"] = { status: 200, data: null };
            var names = null;
            var stored = "unset";
            NomNomz.data.queue().then(function (q) { names = q.map(function (i) { return i.trackName; }).join(","); });
            NomNomz.data.storage("hot potato/holder").then(function (v) { stored = v; });
            """
        );

        engine.Evaluate("names").AsString().Should().Be("A,B");
        engine.Evaluate("stored === null").AsBoolean().Should().BeTrue();
        engine
            .Evaluate("calls[calls.length - 1].url")
            .AsString()
            .Should()
            .Be("/api/v1/overlay/storage/hot%20potato%2Fholder");
    }

    [Theory]
    [InlineData(401, "blocked")]
    [InlineData(403, "blocked")]
    [InlineData(500, "error")]
    public void PlaybackToken_keeps_the_blocked_and_error_difference_and_never_rejects(
        int status,
        string expected
    )
    {
        Engine engine = Booted();
        engine.SetValue("status", status);
        engine.Execute(
            """
            answers["/api/v1/overlay/spotify-token"] = { status: status, data: null };
            var outcome = null;
            NomNomz.spotify.playbackToken().then(function (r) { outcome = r; });
            """
        );

        engine.Evaluate("outcome.error").AsString().Should().Be(expected);
        engine.Evaluate("'token' in outcome").AsBoolean().Should().BeFalse();
    }

    [Fact]
    public void PlaybackToken_resolves_the_token_and_a_network_failure_is_an_error_not_a_rejection()
    {
        Engine engine = Booted();
        engine.Execute(
            """
            answers["/api/v1/overlay/spotify-token"] = { status: 200, data: "spotify-access" };
            var ok = null;
            NomNomz.spotify.playbackToken().then(function (r) { ok = r; });
            answers["/api/v1/overlay/spotify-token"] = "network";
            var down = null;
            NomNomz.spotify.playbackToken().then(function (r) { down = r; });
            """
        );

        engine.Evaluate("ok.token").AsString().Should().Be("spotify-access");
        engine.Evaluate("down.error").AsString().Should().Be("error");
    }

    [Fact]
    public void Widget_exposes_the_id_and_name_without_exposing_the_token()
    {
        Engine engine = Booted();

        engine.Evaluate("NomNomz.widget.id").AsString().Should().Be("widget-1");
        engine.Evaluate("NomNomz.widget.name").AsString().Should().Be("Now Playing");
        engine
            .Evaluate(
                "JSON.stringify(Object.keys(NomNomz.widget)) + JSON.stringify(Object.keys(NomNomz))"
            )
            .AsString()
            .Should()
            .NotContain("oken");
    }
}
