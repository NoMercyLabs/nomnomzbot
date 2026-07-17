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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves a song-request queue change reaches the standing <c>sr_queue</c> overlay widget as an
/// <c>sr_queue</c> widget event carrying the snapshot's { items: [{ title, requestedBy, durationSec }] }
/// shape — and only reaches widgets subscribed to that type.
/// </summary>
public sealed class SrQueueBroadcastHandlerTests
{
    [Fact]
    public async Task Queue_change_reaches_a_subscribed_widget_with_the_snapshot_items()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Widget srQueue = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Up next",
            IsEnabled = true,
            EventSubscriptions = ["sr_queue"],
        };
        Widget bystander = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Now playing",
            IsEnabled = true,
            EventSubscriptions = ["now_playing"],
        };
        db.Widgets.AddRange(srQueue, bystander);
        await db.SaveChangesAsync();
        SrQueueBroadcastHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new SongRequestQueueChangedEvent
            {
                BroadcasterId = channel,
                Items =
                [
                    new SongRequestQueueSnapshotItem("Song A", "viewer1", 210),
                    new SongRequestQueueSnapshotItem("Song B", "viewer2", 185),
                ],
            }
        );

        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                srQueue.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt => evt.EventType == "sr_queue" && ItemsMatch(evt.Data)),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .DidNotReceive()
            .SendWidgetEventAsync(
                channel.ToString(),
                bystander.Id.ToString(),
                Arg.Any<WidgetEventDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>Asserts the payload serializes to the { items: [{ title, requestedBy, durationSec }] } wire shape.</summary>
    private static bool ItemsMatch(object? data)
    {
        if (data is null)
            return false;
        JsonElement json = JsonSerializer.SerializeToElement(
            data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );
        JsonElement items = json.GetProperty("items");
        return items.GetArrayLength() == 2
            && items[0].GetProperty("title").GetString() == "Song A"
            && items[0].GetProperty("requestedBy").GetString() == "viewer1"
            && items[0].GetProperty("durationSec").GetInt32() == 210
            && items[1].GetProperty("title").GetString() == "Song B";
    }
}
