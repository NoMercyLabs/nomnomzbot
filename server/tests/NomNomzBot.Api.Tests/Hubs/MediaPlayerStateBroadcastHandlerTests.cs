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
using NomNomzBot.Domain.MediaShare.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// What the media-share player reports reaches the owning channel's dashboard group as
/// <c>media_share_player_state</c>, so the dashboard player card follows the stream.
/// </summary>
public sealed class MediaPlayerStateBroadcastHandlerTests
{
    [Fact]
    public async Task A_state_change_reaches_the_owning_channels_dashboard_with_the_player_fields()
    {
        IDashboardNotifier dashboard = Substitute.For<IDashboardNotifier>();
        MediaPlayerStateBroadcastHandler handler = new(dashboard);
        Guid channel = Guid.CreateVersion7();
        Guid requestId = Guid.CreateVersion7();

        await handler.HandleAsync(
            new MediaPlayerStateChangedEvent
            {
                BroadcasterId = channel,
                State = "playing",
                RequestId = requestId,
                Title = "Never Gonna",
                SharerName = "viewer1",
                PositionSeconds = 12,
                DurationSeconds = 213,
                Volume = 70,
            }
        );

        await dashboard
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "media_share_player_state",
                Arg.Is<object>(data => Matches(data, requestId)),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>(),
                Arg.Any<string?>()
            );
    }

    [Fact]
    public async Task The_platform_sentinel_channel_notifies_nobody()
    {
        IDashboardNotifier dashboard = Substitute.For<IDashboardNotifier>();
        MediaPlayerStateBroadcastHandler handler = new(dashboard);

        await handler.HandleAsync(
            new MediaPlayerStateChangedEvent { BroadcasterId = Guid.Empty, State = "idle" }
        );

        await dashboard
            .DidNotReceive()
            .NotifyChannelAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>(),
                Arg.Any<string?>()
            );
    }

    private static bool Matches(object? data, Guid requestId)
    {
        JsonElement json = JsonSerializer.SerializeToElement(
            data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );
        return json.GetProperty("state").GetString() == "playing"
            && json.GetProperty("requestId").GetGuid() == requestId
            && json.GetProperty("title").GetString() == "Never Gonna"
            && json.GetProperty("sharerName").GetString() == "viewer1"
            && json.GetProperty("positionSeconds").GetInt32() == 12
            && json.GetProperty("durationSeconds").GetInt32() == 213
            && json.GetProperty("volume").GetInt32() == 70;
    }
}
