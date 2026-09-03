// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// Drops a channel's cached auto-mod rules the moment an operator changes them.
///
/// <para>Without this the cache is only as fresh as its five-minute expiry: an operator turns a rule
/// off and chat keeps being moderated by the old one for up to five minutes, with nothing on screen
/// saying so. "It didn't work" followed by "…now it works" is the worst possible feedback for a
/// safety control.</para>
///
/// <para>Two domains write <c>moderation_rule</c> records — <c>moderation-rules</c> for the rule CRUD
/// and <c>automod</c> for the AutoMod configuration page — so both must evict. A domain missing from
/// this set is a rule change that silently does not take effect.</para>
/// </summary>
public sealed class AutoModRuleCacheInvalidator : IEventHandler<ChannelConfigChangedEvent>
{
    private static readonly HashSet<string> RuleDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "moderation-rules",
        "automod",
    };

    private readonly IAutoModRuleCache _cache;

    public AutoModRuleCacheInvalidator(IAutoModRuleCache cache)
    {
        _cache = cache;
    }

    public Task HandleAsync(ChannelConfigChangedEvent @event, CancellationToken cancellationToken)
    {
        if (RuleDomains.Contains(@event.Domain))
            _cache.Invalidate(@event.BroadcasterId);

        return Task.CompletedTask;
    }
}
