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
