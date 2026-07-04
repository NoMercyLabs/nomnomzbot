// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Evicts a channel's cached chat-decoration rules (chat-decoration spec §0) the instant one of its feature
/// toggles changes, so the very next chat message reflects the new state instead of waiting out the decorator's
/// 60s cache TTL. <see cref="NomNomzBot.Infrastructure.Platform.FeatureService"/> already publishes
/// <see cref="ChannelConfigChangedEvent"/> for <c>Domain == "features"</c> after every successful toggle
/// (save → publish, the config-CRUD convention) — this handler reacts to that existing signal rather than
/// <c>FeatureService</c> reaching into a chat-module cache key itself, keeping the platform-level feature store
/// ignorant of chat's cache scheme. Any "features" toggle for the channel busts the key (not just the four
/// decoration keys) — a harmless extra cache miss on an unrelated toggle (e.g. <c>custom_code</c>), never a
/// correctness issue.
/// </summary>
public sealed class ChatDecorationRulesCacheInvalidator : IEventHandler<ChannelConfigChangedEvent>
{
    private readonly ICacheService _cache;

    public ChatDecorationRulesCacheInvalidator(ICacheService cache)
    {
        _cache = cache;
    }

    public Task HandleAsync(
        ChannelConfigChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || @event.Domain != "features")
            return Task.CompletedTask;

        return _cache.RemoveAsync(
            ChatDecorationRulesCacheKeys.Channel(@event.BroadcasterId),
            cancellationToken
        );
    }
}
