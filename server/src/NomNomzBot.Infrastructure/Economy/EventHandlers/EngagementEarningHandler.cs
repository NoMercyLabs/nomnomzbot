// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Economy.EventHandlers;

/// <summary>
/// Awards currency for engagement milestones: follows, new subscriptions, cheers, and incoming raids.
/// Each engagement maps to a named <see cref="NomNomzBot.Domain.Economy.Enums.EarningSource"/> — the channel must
/// have the corresponding <c>EarningRule</c> enabled for earning to occur (economy.md §3.3). The earn is idempotent
/// via the domain event's <c>EventId</c> so duplicate EventSub deliveries are no-ops.
/// </summary>
public sealed class EngagementEarningHandler(
    ICurrencyEarningService earning,
    IUserService userService
)
    : IEventHandler<FollowEvent>,
        IEventHandler<NewSubscriptionEvent>,
        IEventHandler<CheerEvent>,
        IEventHandler<RaidReceivedEvent>
{
    public async Task HandleAsync(FollowEvent @event, CancellationToken cancellationToken = default)
    {
        Guid? viewerUserId = await ResolveViewerIdAsync(
            @event.UserId,
            @event.UserLogin,
            @event.UserDisplayName,
            cancellationToken
        );
        if (viewerUserId is null)
            return;

        await earning.ApplyEarningAsync(
            @event.BroadcasterId,
            new EarnRequest(viewerUserId.Value, "Follow", 1, @event.EventId, null, null),
            cancellationToken
        );
    }

    public async Task HandleAsync(
        NewSubscriptionEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        Guid? viewerUserId = await ResolveViewerIdAsync(
            @event.UserId,
            @event.UserDisplayName.ToLowerInvariant(),
            @event.UserDisplayName,
            cancellationToken
        );
        if (viewerUserId is null)
            return;

        await earning.ApplyEarningAsync(
            @event.BroadcasterId,
            new EarnRequest(viewerUserId.Value, "Subscription", 1, @event.EventId, null, null),
            cancellationToken
        );
    }

    public async Task HandleAsync(CheerEvent @event, CancellationToken cancellationToken = default)
    {
        Guid? viewerUserId = await ResolveViewerIdAsync(
            @event.UserId,
            @event.UserDisplayName.ToLowerInvariant(),
            @event.UserDisplayName,
            cancellationToken
        );
        if (viewerUserId is null)
            return;

        // Cheer units = actual bits cheered so the rate multiplier is applied against the real donation size.
        await earning.ApplyEarningAsync(
            @event.BroadcasterId,
            new EarnRequest(viewerUserId.Value, "Cheer", @event.Bits, @event.EventId, null, null),
            cancellationToken
        );
    }

    public async Task HandleAsync(
        RaidReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        Guid? viewerUserId = await ResolveViewerIdAsync(
            @event.FromUserId,
            @event.FromDisplayName.ToLowerInvariant(),
            @event.FromDisplayName,
            cancellationToken
        );
        if (viewerUserId is null)
            return;

        // Raid units = viewer count — lets the rate scale with raid size if desired.
        await earning.ApplyEarningAsync(
            @event.BroadcasterId,
            new EarnRequest(
                viewerUserId.Value,
                "Raid",
                @event.ViewerCount,
                @event.EventId,
                null,
                null
            ),
            cancellationToken
        );
    }

    private async Task<Guid?> ResolveViewerIdAsync(
        string twitchUserId,
        string login,
        string displayName,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(twitchUserId))
            return null;

        Result<UserDto> result = await userService.GetOrCreateAsync(
            twitchUserId,
            login,
            displayName,
            ct
        );
        if (result.IsFailure || !Guid.TryParse(result.Value.Id, out Guid id))
            return null;

        return id;
    }
}
