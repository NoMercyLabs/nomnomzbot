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
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Stream.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the shoutout broadcasters forward the outgoing/incoming shoutout to dashboard clients over the generic
/// <c>ChannelEvent</c> taxonomy — these events had no handler of any kind before this slice (translators were
/// subscribed but nothing consumed them).
/// </summary>
public sealed class ShoutoutBroadcastHandlersTests
{
    [Fact]
    public async Task ShoutoutSent_MapsRecipient_AsShoutoutSentChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ShoutoutSentBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new ShoutoutSentEvent
            {
                BroadcasterId = channel,
                ToUserId = "target-1",
                ToDisplayName = "TargetStreamer",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "shoutout_sent",
                Arg.Is<object>(data =>
                    data is ShoutoutSentAlertDto
                    && ((ShoutoutSentAlertDto)data).ToUserId == "target-1"
                    && ((ShoutoutSentAlertDto)data).ToDisplayName == "TargetStreamer"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ShoutoutReceived_MapsSourceAndViewerCount_AsShoutoutReceivedChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        ShoutoutReceivedBroadcastHandler handler = new(notifier, enricher);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new ShoutoutReceivedEvent
            {
                BroadcasterId = channel,
                FromBroadcasterId = "source-1",
                FromBroadcasterDisplayName = "SourceStreamer",
                FromBroadcasterLogin = "sourcestreamer",
                ViewerCount = 42,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "shoutout_received",
                Arg.Is<object>(data =>
                    data is ShoutoutReceivedAlertDto
                    && ((ShoutoutReceivedAlertDto)data).FromBroadcasterId == "source-1"
                    && ((ShoutoutReceivedAlertDto)data).FromBroadcasterDisplayName
                        == "SourceStreamer"
                    && ((ShoutoutReceivedAlertDto)data).FromBroadcasterLogin == "sourcestreamer"
                    && ((ShoutoutReceivedAlertDto)data).ViewerCount == 42
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ShoutoutReceived_WithKnownBroadcaster_CarriesTheEnrichedFields()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "source-1", Arg.Any<CancellationToken>())
            .Returns(new HubUserEnrichment("SourceStreamer", "https://cdn/avatar.png", null, null));
        ShoutoutReceivedBroadcastHandler handler = new(notifier, enricher);

        await handler.HandleAsync(
            new ShoutoutReceivedEvent
            {
                BroadcasterId = channel,
                FromBroadcasterId = "source-1",
                FromBroadcasterDisplayName = "SourceStreamer",
                FromBroadcasterLogin = "sourcestreamer",
                ViewerCount = 42,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "shoutout_received",
                Arg.Is<object>(data =>
                    data is ShoutoutReceivedAlertDto
                    && ((ShoutoutReceivedAlertDto)data).AvatarUrl == "https://cdn/avatar.png"
                    && ((ShoutoutReceivedAlertDto)data).Pronouns == null
                    && ((ShoutoutReceivedAlertDto)data).CommunityStanding == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ShoutoutSent_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ShoutoutSentBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new ShoutoutSentEvent
            {
                BroadcasterId = Guid.Empty,
                ToUserId = "target-1",
                ToDisplayName = "x",
            }
        );

        await notifier
            .DidNotReceive()
            .NotifyChannelAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>()
            );
    }
}
