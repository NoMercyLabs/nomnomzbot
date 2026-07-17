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
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// The SuperMod PLATFORM nuke (moderation.md §3.4, J.2a) — a cross-channel mass ban over TENANT channels,
/// executed on each channel's own token regardless of the actor's per-channel Twitch mod status (distinct
/// from <see cref="IOperatorNetworkBanService"/>, which rides the operator's own Twitch moderator rights).
/// The channel set = every enabled, onboarded tenant channel where the ACTOR's resolved effective level is
/// SuperMod(20)+ — re-checked in-process per channel, never the HTTP gate alone. No mass-reporting.
/// </summary>
public interface INetworkNukeService
{
    /// <summary>
    /// Bans the target across the actor's SuperMod+ channels: one <c>NetworkNukeBatch</c>, one
    /// <c>moderation_action(nuke, origin=network_nuke, NetworkNukeBatchId)</c> record per successful leg.
    /// Partial leg failures → <c>Status=partial</c>. <c>RequireConfirmation</c> must be true
    /// (<c>VALIDATION_FAILED</c> otherwise — the single-confirmation guardrail).
    /// </summary>
    Task<Result<NetworkNukeBatchDto>> NukeAsync(
        Guid originBroadcasterId,
        Guid actorUserId,
        NetworkNukeRequest request,
        CancellationToken ct = default
    );

    /// <summary>The one-shot reversal (un-nuke): lifts the ban on every recorded leg, marks the batch reverted.</summary>
    Task<Result<NetworkNukeBatchDto>> RevertAsync(
        Guid actorUserId,
        Guid batchId,
        CancellationToken ct = default
    );

    /// <summary>The origin channel's batch history, newest first.</summary>
    Task<Result<PagedList<NetworkNukeBatchDto>>> ListBatchesAsync(
        Guid originBroadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
