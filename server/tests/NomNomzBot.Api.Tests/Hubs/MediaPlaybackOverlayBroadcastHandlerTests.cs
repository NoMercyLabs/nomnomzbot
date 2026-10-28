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

using System.Text.Json;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.MediaShare.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A playback command the server issues reaches the media-share overlay as a <c>media_share_command</c>
/// widget event, carrying the item to load, and reaches only widgets of that channel that subscribe to it.
/// </summary>
public sealed class MediaPlaybackOverlayBroadcastHandlerTests
{
    private static Widget WidgetOf(Guid channel, params string[] subscriptions) =>
        new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Media share",
            IsEnabled = true,
            EventSubscriptions = [.. subscriptions],
        };

    [Fact]
    public async Task A_load_command_reaches_the_subscribed_widget_with_the_item_and_no_other_widget()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Guid requestId = Guid.CreateVersion7();
        Widget player = WidgetOf(channel, "media_share_command");
        Widget bystander = WidgetOf(channel, "sr_queue");
        Widget foreign = WidgetOf(Guid.CreateVersion7(), "media_share_command");
        db.Widgets.AddRange(player, bystander, foreign);
        await db.SaveChangesAsync();
        MediaPlaybackOverlayBroadcastHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new MediaPlayerCommandIssuedEvent
            {
                BroadcasterId = channel,
                Command = "load",
                RequestId = requestId,
                SourceType = "youtube",
                MediaRef = "dQw4w9WgXcQ",
                SourceUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                Title = "Never Gonna",
                DurationSeconds = 213,
            }
        );

        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                player.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "media_share_command" && IsLoadOf(evt.Data, requestId)
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .DidNotReceive()
            .SendWidgetEventAsync(
                Arg.Any<string>(),
                bystander.Id.ToString(),
                Arg.Any<WidgetEventDto>(),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .DidNotReceive()
            .SendWidgetEventAsync(
                Arg.Any<string>(),
                foreign.Id.ToString(),
                Arg.Any<WidgetEventDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_seek_command_carries_the_position_and_no_item()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Widget player = WidgetOf(channel, "media_share_command");
        db.Widgets.Add(player);
        await db.SaveChangesAsync();
        MediaPlaybackOverlayBroadcastHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new MediaPlayerCommandIssuedEvent
            {
                BroadcasterId = channel,
                Command = "seek",
                PositionSeconds = 42,
            }
        );

        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                player.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt => IsSeekTo42(evt.Data)),
                Arg.Any<CancellationToken>()
            );
    }

    private static JsonElement Wire(object? data) =>
        JsonSerializer.SerializeToElement(data, OverlayWireJson.Options);

    private static bool IsLoadOf(object? data, Guid requestId)
    {
        JsonElement json = Wire(data);
        return json.GetProperty("command").GetString() == "load"
            && json.GetProperty("requestId").GetGuid() == requestId
            && json.GetProperty("sourceType").GetString() == "youtube"
            && json.GetProperty("mediaRef").GetString() == "dQw4w9WgXcQ"
            && json.GetProperty("title").GetString() == "Never Gonna"
            && json.GetProperty("durationSeconds").GetInt32() == 213;
    }

    private static bool IsSeekTo42(object? data)
    {
        JsonElement json = Wire(data);
        return json.GetProperty("command").GetString() == "seek"
            && json.GetProperty("positionSeconds").GetInt32() == 42
            && json.GetProperty("mediaRef").ValueKind == JsonValueKind.Null;
    }
}
