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
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The stream recap must stay on screen for at least 60 seconds, and the raid flow must leave the streamer's own
/// ending scene its 30 seconds after the recap fades: nothing in the start flow raids, waits or switches away
/// before the recap hold plus the ending scene have run.
/// </summary>
public sealed class RaidRecapHoldTests
{
    private static FirstPartyWidgetDefinition Recap =>
        FirstPartyWidgetCatalogue.All.Single(widget => widget.Key == "raid_recap");

    [Fact]
    public void A_new_raid_recap_widget_holds_for_sixty_seconds()
    {
        Recap.DefaultSettings["holdSeconds"].Should().Be(60);
    }

    [Fact]
    public void The_recap_hold_and_the_ending_scene_fit_inside_the_raid_countdown()
    {
        int hold = Convert.ToInt32(Recap.DefaultSettings["holdSeconds"]);

        (hold + RaidStartFlowSeeder.EndingSceneSeconds)
            .Should()
            .BeLessThanOrEqualTo(
                RaidStartFlowSeeder.MinRaidCountdownSeconds,
                "the recap (hold) plus the ending scene must end no later than Twitch commits the raid"
            );
        RaidStartFlowSeeder.EndingSceneSeconds.Should().Be(30);
    }

    [Fact]
    public void The_start_flow_shows_the_recap_and_the_ending_scene_together_and_never_switches_away()
    {
        using JsonDocument graph = JsonDocument.Parse(
            RaidStartFlowSeeder.BuildPlatformContentPayloadJson()
        );

        List<string> actions = graph
            .RootElement.GetProperty("steps")
            .EnumerateArray()
            .Select(step => step.GetProperty("action").GetProperty("type").GetString()!)
            .ToList();

        actions
            .Should()
            .Equal(
                ["show_raid_recap", "obs_switch_scene", "send_message"],
                "no wait, raid or second scene switch may cut the recap or the ending scene short"
            );
    }
}
