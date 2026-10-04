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
/// A script's <c>widget.emit(widget, type, data)</c> may send <c>false</c>, <c>0</c> or an empty string. The overlay
/// and the editor preview hand the widget handler exactly that value; only a missing or null payload becomes <c>{}</c>.
/// </summary>
public sealed class WidgetEmitFalsyDataTests
{
    private const string Record =
        "var got = []; NomNomz.on('t', function (d) { got.push(typeof d + ':' + JSON.stringify(d)); });";

    [Theory]
    [InlineData(false, "boolean:false")]
    [InlineData(0, "number:0")]
    [InlineData("", "string:\"\"")]
    [InlineData(7, "number:7")]
    public void The_overlay_hands_the_widget_a_falsy_value_unchanged(object data, string expected)
    {
        OverlaySdkRuntime runtime = OverlaySdkRuntime.Connected();
        runtime.Evaluate(Record);

        runtime.Deliver(
            "WidgetEvent",
            new Dictionary<string, object> { ["eventType"] = "t", ["data"] = data }
        );

        runtime.Text("got.join('|')").Should().Be(expected);
    }

    [Fact]
    public void The_overlay_turns_null_data_into_an_empty_object()
    {
        OverlaySdkRuntime runtime = OverlaySdkRuntime.Connected();
        runtime.Evaluate(Record);

        runtime.Deliver(
            "WidgetEvent",
            new Dictionary<string, object?> { ["eventType"] = "t", ["data"] = null }
        );

        runtime.Text("got.join('|')").Should().Be("object:{}");
    }

    [Fact]
    public void The_overlay_turns_missing_data_into_an_empty_object()
    {
        OverlaySdkRuntime runtime = OverlaySdkRuntime.Connected();
        runtime.Evaluate(Record);

        runtime.Deliver("WidgetEvent", new Dictionary<string, object> { ["eventType"] = "t" });

        runtime.Text("got.join('|')").Should().Be("object:{}");
    }

    private static Engine Preview()
    {
        Engine engine = new();
        engine.Execute(
            """
            var window = this;
            var __listeners = {};
            window.addEventListener = function (type, fn) { (__listeners[type] = __listeners[type] || []).push(fn); };
            window.parent = { postMessage: function () {} };
            function __post(data) { (__listeners.message || []).forEach(function (fn) { fn({ data: data }); }); }
            """
        );
        engine.Execute(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "editor", "preview-sdk.js")
            )
        );
        engine.Execute(Record);
        return engine;
    }

    [Theory]
    [InlineData("false", "boolean:false")]
    [InlineData("0", "number:0")]
    [InlineData("''", "string:\"\"")]
    [InlineData("null", "object:{}")]
    [InlineData("undefined", "object:{}")]
    public void The_preview_hands_the_widget_a_falsy_value_unchanged_and_only_null_or_missing_becomes_empty(
        string data,
        string expected
    )
    {
        Engine preview = Preview();

        preview.Execute($"__post({{ __nnzFire: {{ type: 't', data: {data} }} }});");

        preview.Evaluate("got.join('|')").ToString().Should().Be(expected);
    }
}
