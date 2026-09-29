// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// One permission model for the moderating music chat commands (<c>!skip</c>, <c>!volume</c>,
/// <c>!bansong</c>). Their dashboard twins (<c>POST /music/skip</c>, pause, ban, …) are guarded by the Gate-2
/// action <see cref="ActionKey"/>, so chat asks the SAME question through
/// <see cref="IRoleResolver.HasCapabilityAsync"/> — the resolver behind the HTTP gate. A moderator badge
/// passes on the live chat role; anyone else passes only when the broadcaster granted them the action.
/// </summary>
public sealed class MusicModerationGate(IUserService users, IRoleResolver roles)
{
    /// <summary>The Gate-2 action key that guards queue/playback moderation on the REST surface.</summary>
    public const string ActionKey = "music:queue:moderate";

    public async Task<bool> IsAllowedAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        if (context.RoleLevel >= PermissionLevel.Moderator.ToLevelValue())
            return true;

        Result<UserDto> caller = await users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            cancellationToken: ct
        );
        if (caller.IsFailure || !Guid.TryParse(caller.Value.Id, out Guid userId))
            return false;

        Result<bool> capability = await roles.HasCapabilityAsync(
            userId,
            context.BroadcasterId,
            ActionKey,
            ct
        );
        return capability is { IsSuccess: true, Value: true };
    }
}
