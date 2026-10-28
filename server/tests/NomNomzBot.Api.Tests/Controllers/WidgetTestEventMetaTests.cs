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

using FluentAssertions;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The overlay hands a widget handler <c>meta.test = true</c> for a dashboard test fire, so a widget that keeps a
/// value or a list can show the test without keeping it. A live event carries <c>meta.test = false</c>.
/// </summary>
public sealed class WidgetTestEventMetaTests
{
    private const string Record =
        "var got = []; NomNomz.on('follow', function (d, t, meta) { got.push(d.name + ':' + (meta && meta.test === true)); });";

    [Fact]
    public void A_test_fire_reaches_the_handler_marked_test_and_a_live_event_does_not()
    {
        OverlaySdkRuntime runtime = OverlaySdkRuntime.Connected();
        runtime.Evaluate(Record);

        runtime.Deliver(
            "WidgetEvent",
            new Dictionary<string, object>
            {
                ["eventType"] = "follow",
                ["data"] = new Dictionary<string, object> { ["name"] = "TestFollower" },
                ["isTest"] = true,
            }
        );
        runtime.Deliver(
            "WidgetEvent",
            new Dictionary<string, object>
            {
                ["eventType"] = "follow",
                ["data"] = new Dictionary<string, object> { ["name"] = "RealFollower" },
                ["isTest"] = false,
            }
        );

        runtime.Text("got.join('|')").Should().Be("TestFollower:true|RealFollower:false");
    }
}
