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
using NomNomzBot.Domain.Community.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="FollowBroadcastHandler"/> carries the GAP E3-2 hub-broadcast-layer enrichment
/// (avatar/pronouns/community standing) additively on <see cref="FollowAlertDto"/> — populated when the
/// enricher has data, and <c>null</c> (never a crash) when it doesn't.
/// </summary>
public sealed class FollowBroadcastHandlerTests
{
    [Fact]
    public async Task Follow_with_known_viewer_carries_the_enriched_fields()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns(
                new HubUserEnrichment("Stoney", "https://cdn/avatar.png", "they/them", "Subscriber")
            );
        FollowBroadcastHandler handler = new(notifier, enricher);

        await handler.HandleAsync(
            new FollowEvent
            {
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "Stoney",
                UserLogin = "stoney_eagle",
                FollowedAt = DateTimeOffset.UtcNow,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "follow",
                Arg.Is<object>(data =>
                    data is FollowAlertDto
                    && ((FollowAlertDto)data).AvatarUrl == "https://cdn/avatar.png"
                    && ((FollowAlertDto)data).Pronouns == "they/them"
                    && ((FollowAlertDto)data).CommunityStanding == "Subscriber"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Follow_with_unknown_viewer_carries_null_enrichment_not_a_crash()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns((HubUserEnrichment?)null);
        FollowBroadcastHandler handler = new(notifier, enricher);

        await handler.HandleAsync(
            new FollowEvent
            {
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "Stoney",
                UserLogin = "stoney_eagle",
                FollowedAt = DateTimeOffset.UtcNow,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "follow",
                Arg.Is<object>(data =>
                    data is FollowAlertDto
                    && ((FollowAlertDto)data).AvatarUrl == null
                    && ((FollowAlertDto)data).Pronouns == null
                    && ((FollowAlertDto)data).CommunityStanding == null
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
