// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// A <c>channel.follow</c> event carries the follow date, so the follow cache learns it directly: the
/// next chat message from a fresh follower sees the follow without a Helix call.
/// </summary>
public sealed class FollowStateCacheHandler : IEventHandler<FollowEvent>
{
    private readonly FollowStateCache _cache;

    public FollowStateCacheHandler(FollowStateCache cache)
    {
        _cache = cache;
    }

    public Task HandleAsync(FollowEvent @event, CancellationToken ct = default)
    {
        if (
            @event.BroadcasterId != Guid.Empty
            && @event.Provider == AuthEnums.Platform.Twitch
            && !string.IsNullOrWhiteSpace(@event.UserId)
        )
            _cache.Set(
                @event.BroadcasterId,
                @event.UserId,
                new FollowLookup(FollowState.Following, @event.FollowedAt)
            );

        return Task.CompletedTask;
    }
}
