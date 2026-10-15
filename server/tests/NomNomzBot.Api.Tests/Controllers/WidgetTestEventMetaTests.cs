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
