// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts follow alerts to dashboard/overlay clients.</summary>
public sealed class FollowBroadcastHandler : IEventHandler<FollowEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;

    public FollowBroadcastHandler(IDashboardNotifier notifier, IHubUserEnricher enricher)
    {
        _notifier = notifier;
        _enricher = enricher;
    }

    public async Task HandleAsync(FollowEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.UserId,
            ct
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "follow",
            new FollowAlertDto(
                @event.UserId,
                @event.UserDisplayName,
                @event.UserLogin,
                @event.FollowedAt,
                enrichment?.AvatarUrl,
                enrichment?.Pronouns,
                enrichment?.CommunityStanding
            ),
            ct,
            userId: @event.UserId,
            userDisplayName: @event.UserDisplayName
        );
    }
}
