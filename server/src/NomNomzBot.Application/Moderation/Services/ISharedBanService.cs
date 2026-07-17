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
/// The shared-chat ban trust web (moderation.md §3.5, schema J.9/J.9a) — per-channel OPT-IN policy plus the
/// directional allow-list of partner channels whose shared-chat bans this channel accepts. Everything is
/// default-deny: both switches default OFF and an empty trust list accepts nothing. Writes are SuperMod-tier
/// (Gate-2 <c>moderation:sharedban:write</c>) and the service re-verifies the floor in-process via
/// <c>IRoleResolver.ResolveEffectiveLevelAsync ≥ SuperMod(20)</c> — never trust the gate alone.
/// </summary>
public interface ISharedBanService
{
    /// <summary>The channel's policy + trust list; a channel with no row reads as the defaults (both OFF, empty list).</summary>
    Task<Result<SharedBanSettingsDto>> GetSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Upserts the policy row (both switches explicit — no partial writes).</summary>
    Task<Result<SharedBanSettingsDto>> SaveSettingsAsync(
        Guid broadcasterId,
        Guid actorUserId,
        SaveSharedBanSettingsRequest request,
        CancellationToken ct = default
    );

    /// <summary>Adds a partner to the trust list. Idempotent — re-adding an existing partner returns the existing row.</summary>
    Task<Result<SharedBanTrustedChannelDto>> AddTrustedChannelAsync(
        Guid broadcasterId,
        Guid actorUserId,
        Guid trustedChannelId,
        CancellationToken ct = default
    );

    /// <summary>Removes a partner from the trust list; <c>NOT_FOUND</c> if absent.</summary>
    Task<Result> RemoveTrustedChannelAsync(
        Guid broadcasterId,
        Guid actorUserId,
        Guid trustedChannelId,
        CancellationToken ct = default
    );
}
