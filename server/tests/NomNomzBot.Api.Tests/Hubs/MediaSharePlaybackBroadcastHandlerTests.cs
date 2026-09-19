// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Domain.MediaShare.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="MediaSharePlaybackBroadcastHandler"/> forwards a played/skipped/rejected/approved playback
/// transition to ONLY the owning channel's dashboard group — the gap this closes (S-OBS-09b): a mod approving,
/// skipping, or marking a clip played from one dashboard session left every OTHER open session's Media-Share
/// queue stale (a played clip lingered) until a manual reload, because
/// <see cref="MediaSharePlaybackChangedEvent"/> had no hub broadcaster at all.
/// </summary>
public sealed class MediaSharePlaybackBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_Played_ForwardsRequestIdAndStatusToTheOwningChannelOnly()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        MediaSharePlaybackBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        Guid otherChannel = Guid.CreateVersion7();
        Guid requestId = Guid.CreateVersion7();

        await handler.HandleAsync(
            new MediaSharePlaybackChangedEvent
            {
                BroadcasterId = channel,
                RequestId = requestId,
                Status = "played",
            }
        );

        // Reaches the owning channel's group, carrying the request id + new status...
        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "media_share_playback_changed",
                Arg.Is<object>(data => RequestMatches(data, requestId, "played")),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>(),
                Arg.Any<string?>()
            );
        // ...and never a DIFFERENT channel's group — this is a per-tenant push, not a broadcast.
        await notifier
            .DidNotReceive()
            .NotifyChannelAsync(
                otherChannel.ToString(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>(),
                Arg.Any<string?>()
            );
    }

    [Fact]
    public async Task HandleAsync_Skipped_ForwardsSkippedStatus()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        MediaSharePlaybackBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        Guid requestId = Guid.CreateVersion7();

        await handler.HandleAsync(
            new MediaSharePlaybackChangedEvent
            {
                BroadcasterId = channel,
                RequestId = requestId,
                Status = "skipped",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "media_share_playback_changed",
                Arg.Is<object>(data => RequestMatches(data, requestId, "skipped")),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>(),
                Arg.Any<string?>()
            );
    }

    [Fact]
    public async Task HandleAsync_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        MediaSharePlaybackBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new MediaSharePlaybackChangedEvent
            {
                BroadcasterId = Guid.Empty,
                RequestId = Guid.CreateVersion7(),
                Status = "played",
            }
        );

        await notifier
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

    private static bool RequestMatches(object? data, Guid requestId, string status)
    {
        if (data is null)
            return false;
        System.Reflection.PropertyInfo? requestIdProp = data.GetType().GetProperty("requestId");
        System.Reflection.PropertyInfo? statusProp = data.GetType().GetProperty("status");
        return requestIdProp?.GetValue(data) is Guid actualId
            && actualId == requestId
            && statusProp?.GetValue(data) as string == status;
    }
}
