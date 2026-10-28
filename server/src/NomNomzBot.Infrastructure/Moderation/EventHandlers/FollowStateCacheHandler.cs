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
