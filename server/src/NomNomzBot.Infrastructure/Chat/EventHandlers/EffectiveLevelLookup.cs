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
