// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// The role a channel requires to request a song from one music service. Each service has its own action key
/// (<c>music:request:spotify</c>, <c>music:request:youtube</c>), so a channel can leave Spotify open to everyone
/// and keep YouTube to a trusted role. The level is the action's effective level: the channel's override, else
/// the platform default, else the shipped default (<see cref="ActionLevelPolicy"/>). An unseeded or unknown key
/// fails closed, like Gate 2.
/// </summary>
internal static class SongRequestSourceAccess
{
    public const string RefusalCode = "SOURCE_ROLE_REQUIRED";

    public static string ActionKeyFor(string providerKey) => $"music:request:{providerKey}";

    /// <summary>
    /// A failure when <paramref name="requesterRoleLevel"/> is below the level the channel set for the
    /// service, else <c>null</c>. A caller with no known role (null) counts as Everyone.
    /// </summary>
    public static async Task<Result?> RefuseIfBelowAsync(
        IApplicationDbContext db,
        Guid tenantId,
        string providerKey,
        int? requesterRoleLevel,
        CancellationToken cancellationToken
    )
    {
        string actionKey = ActionKeyFor(providerKey);
        ActionDefinition? action = await db
            .ActionDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ActionKey == actionKey, cancellationToken);
        if (action is null)
            return Refusal(providerKey);

        int? overrideLevel = await db
            .ChannelActionOverrides.AsNoTracking()
            .Where(o =>
                o.BroadcasterId == tenantId
                && o.ActionDefinitionId == action.Id
                && o.DeletedAt == null
            )
            .Select(o => (int?)o.OverrideLevel)
            .FirstOrDefaultAsync(cancellationToken);

        int required = ActionLevelPolicy.EffectiveRequiredLevel(action, overrideLevel);
        return (requesterRoleLevel ?? 0) >= required ? null : Refusal(providerKey);
    }

    private static Result Refusal(string providerKey) =>
        Result.Failure(
            $"Your role can't request {providerKey} songs in this channel.",
            RefusalCode
        );
}
