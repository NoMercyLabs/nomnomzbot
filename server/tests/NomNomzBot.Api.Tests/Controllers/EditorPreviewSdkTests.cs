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

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The editor preview runs a widget against <c>Assets/editor/preview-sdk.js</c> instead of the live overlay SDK.
/// A widget that works in the preview must work on stream, so the preview SDK has the live surface, and what
/// the widget does that would reach the bot is shown to the author instead of vanishing.
/// </summary>
public sealed class EditorPreviewSdkTests
{
    private const string Window = """
        var window = this;
        var __listeners = {};
        window.addEventListener = function (type, fn) { (__listeners[type] = __listeners[type] || []).push(fn); };
        var __told = [];
        window.parent = { postMessage: function (m) { __told.push(m.__nnzPreview); } };
        function __post(data) { (__listeners.message || []).forEach(function (fn) { fn({ data: data }); }); }
        window.WIDGET_SETTINGS = { color: "red" };
        """;

    private static Engine Preview()
    {
        Engine engine = new();
        engine.Execute(Window);
        engine.Execute(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "editor", "preview-sdk.js")
            )
        );
        return engine;
    }

    private static string Text(Engine engine, string script) => engine.Evaluate(script).ToString();

    [Fact]
    public void The_preview_sdk_has_exactly_the_live_sdk_surface()
    {
        OverlaySdkRuntime live = OverlaySdkRuntime.Connected();
        Engine preview = Preview();
        const string surface =
            "Object.keys(window.NomNomz).sort().join(',') + '|' + ['actions', 'data', 'spotify', 'widget']"
            + ".map(function (k) { return k + ':' + Object.keys(window.NomNomz[k]).sort().join(','); }).join('|')";

        Text(preview, surface).Should().Be(live.Text(surface));
    }

    [Fact]
    public void A_data_read_in_the_preview_answers_a_sample_and_is_shown_to_the_author_and_never_fetches()
    {
        Engine preview = Preview();
        preview.Execute(
            """
            var fetch = function () { throw new Error("the preview must not touch the network"); };
            var track = null; var queue = null; var stored = "unset"; var spotify = null;
            NomNomz.data.nowPlaying().then(function (d) { track = d.track; });
            NomNomz.data.queue().then(function (q) { queue = q.length; });
            NomNomz.data.storage("holder").then(function (v) { stored = v; });
            NomNomz.spotify.playbackToken().then(function (r) { spotify = r.error; });
            """
        );

        Text(preview, "track").Should().Be("Preview Track");
        Text(preview, "queue").Should().Be("0");
        Text(preview, "stored === null").Should().Be("true");
        Text(preview, "spotify").Should().Be("error");
        Text(
                preview,
                "__told.filter(function (t) { return t.kind === 'data'; }).map(function (t) { return t.what + (t.key ? ' ' + t.key : ''); }).join('|')"
            )
            .Should()
            .Be("nowPlaying|queue|storage holder|spotify.playbackToken");
    }

    [Fact]
    public void A_throwing_handler_is_shown_to_the_author_and_the_other_handlers_still_run()
    {
        Engine preview = Preview();
        preview.Execute(
            """
            var got = null;
            NomNomz.onAny(function () { throw new Error("boom"); });
            NomNomz.on("follow", function (d) { got = d.user; });
            __post({ __nnzFire: { type: "follow", data: { user: "kitte" } } });
            """
        );

        Text(preview, "got").Should().Be("kitte");
        Text(preview, "__told.length").Should().Be("1");
        Text(preview, "__told[0].kind").Should().Be("error");
        Text(preview, "__told[0].message").Should().Be("onAny handler: boom");
    }

    [Fact]
    public void An_action_is_recorded_with_its_parameters_and_never_reaches_a_bot()
    {
        Engine preview = Preview();
        preview.Execute(
            """
            var ok = null;
            NomNomz.actions.invoke("obs_switch_scene", { scene: "BRB" }).then(function (r) { ok = r.success; });
            """
        );

        Text(preview, "ok").Should().Be("true");
        Text(preview, "__told[0].kind").Should().Be("action");
        Text(preview, "__told[0].actionType").Should().Be("obs_switch_scene");
        Text(preview, "__told[0].params.scene").Should().Be("BRB");
    }

    [Fact]
    public void Settings_start_from_the_injected_values_and_follow_editor_changes()
    {
        Engine preview = Preview();
        preview.Execute(
            """
            var seen = [];
            NomNomz.onSettings(function (s) { seen.push(s.color); });
            __post({ __nnzSettings: { color: "blue" } });
            """
        );

        Text(preview, "seen.join(',')").Should().Be("red,blue");
        Text(preview, "NomNomz.settings.color").Should().Be("blue");
    }
}
