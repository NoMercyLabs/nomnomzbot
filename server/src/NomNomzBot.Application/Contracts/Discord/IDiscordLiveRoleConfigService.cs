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

namespace NomNomzBot.Application.Contracts.Discord;

/// <summary>
/// The owner-facing settings of the "currently live" Discord role rule: one rule per (channel, guild
/// connection). The apply/remove behavior lives in <see cref="IDiscordLiveRoleService"/>. A connection
/// with no rule reads an empty list. The role and member cannot change, and the rule cannot be deleted,
/// while the role is applied, because the offline handler would then remove the wrong role or none.
/// </summary>
public interface IDiscordLiveRoleConfigService
{
    Task<Result<IReadOnlyList<DiscordLiveRoleConfigDto>>> GetAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );

    Task<Result<DiscordLiveRoleConfigDto>> CreateAsync(
        Guid broadcasterId,
        Guid connectionId,
        CreateDiscordLiveRoleConfigRequest request,
        CancellationToken ct = default
    );

    Task<Result<DiscordLiveRoleConfigDto>> UpdateAsync(
        Guid broadcasterId,
        Guid connectionId,
        UpdateDiscordLiveRoleConfigRequest request,
        CancellationToken ct = default
    );

    Task<Result> DeleteAsync(Guid broadcasterId, Guid connectionId, CancellationToken ct = default);
}
