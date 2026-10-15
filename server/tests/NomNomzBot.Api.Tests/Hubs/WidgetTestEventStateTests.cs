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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A test fire of an overlay event ("Latest follower: TestFollower") is a preview. It plays on the subscribed
/// widgets, marked as a test, and leaves no stored state behind: no replay-capture row, and the wire envelope tells
/// the widget not to keep the value.
/// </summary>
public sealed class WidgetTestEventStateTests
{
    private static async Task<(WidgetTestDbContext Db, Guid Channel)> ChannelWithFollowWidgetAsync()
    {
        WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        db.Widgets.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "Labels",
                IsEnabled = true,
                EventSubscriptions = ["follow"],
            }
        );
        await db.SaveChangesAsync();
        return (db, channel);
    }

    [Fact]
    public async Task A_test_sample_leaves_no_replay_capture_row_but_a_live_payload_does()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        (WidgetTestDbContext db, Guid channel) = await ChannelWithFollowWidgetAsync();
        await using WidgetTestDbContext _ = db;

        await WidgetAlertDispatch.RouteAsync(
            db,
            widgets,
            channel,
            "follow",
            WidgetTestSamples.For("follow", DateTimeOffset.UtcNow),
            excludeWidgetId: null,
            channelEventId: null,
            CancellationToken.None
        );

        (await db.RenderedAlertCaptures.CountAsync(c => c.BroadcasterId == channel)).Should().Be(0);

        await WidgetAlertDispatch.RouteAsync(
            db,
            widgets,
            channel,
            "follow",
            new { displayName = "RealFollower" },
            excludeWidgetId: null,
            channelEventId: null,
            CancellationToken.None
        );

        (await db.RenderedAlertCaptures.CountAsync(c => c.BroadcasterId == channel)).Should().Be(1);
    }

    [Fact]
    public async Task A_test_sample_reaches_the_widget_marked_as_a_test_and_a_live_payload_does_not()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        (WidgetTestDbContext db, Guid channel) = await ChannelWithFollowWidgetAsync();
        await using WidgetTestDbContext _ = db;

        await WidgetAlertDispatch.RouteAsync(
            db,
            widgets,
            channel,
            "follow",
            WidgetTestSamples.For("follow", DateTimeOffset.UtcNow),
            excludeWidgetId: null,
            channelEventId: null,
            CancellationToken.None
        );
        await WidgetAlertDispatch.RouteAsync(
            db,
            widgets,
            channel,
            "follow",
            new { displayName = "RealFollower" },
            excludeWidgetId: null,
            channelEventId: null,
            CancellationToken.None
        );

        List<WidgetEventDto> sent = widgets
            .ReceivedCalls()
            .Where(call =>
                call.GetMethodInfo().Name == nameof(IWidgetNotifier.SendWidgetEventAsync)
            )
            .Select(call => (WidgetEventDto)call.GetArguments()[2]!)
            .ToList();
        sent.Should().HaveCount(2);
        sent[0].IsTest.Should().BeTrue();
        sent[1].IsTest.Should().BeFalse();
    }
}
