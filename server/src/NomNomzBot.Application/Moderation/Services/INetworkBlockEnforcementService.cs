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

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Keeps an enforced network block true after it was applied. <see cref="INetworkBlockService.ApplyAsync"/>
/// bans the actor only in the tenants it finds at that moment; this service adds a ban leg in every tenant
/// the actor reaches later — a channel onboarded after the block, or a channel where the actor first chats
/// after it. Each new leg carries the block id, so a lift reverses it with the rest.
/// System-side: no operator acts here, so there is no IAM gate — the gate ran when the block was applied.
/// </summary>
public interface INetworkBlockEnforcementService
{
    /// <summary>
    /// Chat ingest: bans <paramref name="twitchUserId"/> in <paramref name="broadcasterId"/> when an enforced
    /// block names them and this channel has no leg yet. Returns how many legs were added (0 or 1).
    /// </summary>
    Task<Result<int>> EnforceForChatterAsync(
        Guid broadcasterId,
        string twitchUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Onboarding: bans, in <paramref name="broadcasterId"/>, every actor whose block was applied before this
    /// channel was created. A channel that already existed when a block was applied is not touched here — the
    /// startup backfill re-publishes onboarding for every channel, and it must never widen a block past the
    /// tenants the operator confirmed; chat ingest covers those channels. Returns how many legs were added.
    /// </summary>
    Task<Result<int>> EnforceForOnboardedChannelAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
