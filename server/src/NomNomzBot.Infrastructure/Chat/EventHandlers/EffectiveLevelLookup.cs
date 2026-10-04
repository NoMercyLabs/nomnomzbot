// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// One chat message's effective level on the unified ladder: MAX(live badge level, resolver level).
/// The badge comes first; the resolver (bot-granted roles, <c>!permit</c> grants, community standing) runs
/// only when a caller needs more than the badge gives, and at most once per message. A broadcaster badge
/// never asks the resolver, and a resolver that cannot answer fails closed to the badge level.
/// </summary>
internal sealed class EffectiveLevelLookup(int badgeLevel, Func<Task<int?>> resolve)
{
    private static readonly int BroadcasterLevel = PermissionLevel.Broadcaster.ToLevelValue();

    private bool _resolverAsked;
    private int? _resolved;

    public async Task<int> GetAsync()
    {
        if (badgeLevel >= BroadcasterLevel)
            return badgeLevel;

        if (!_resolverAsked)
        {
            _resolved = await resolve();
            _resolverAsked = true;
        }

        return Math.Max(badgeLevel, _resolved ?? badgeLevel);
    }

    public async Task<bool> MeetsAsync(int floor) =>
        badgeLevel >= floor || await GetAsync() >= floor;
}
