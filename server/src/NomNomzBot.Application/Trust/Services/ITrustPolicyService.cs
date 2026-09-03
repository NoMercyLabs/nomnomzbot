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
using NomNomzBot.Application.Trust.Dtos;
using NomNomzBot.Domain.Trust.Entities;

namespace NomNomzBot.Application.Trust.Services;

/// <summary>
/// Reads a channel's trust tuning (S-OWN23). Every consumer of
/// <see cref="Domain.Trust.TrustScoreCalculator"/> resolves the policy through here, so the moderation
/// projection and song-request gating always score a viewer the same way — one calculator, one policy,
/// never a fork.
/// </summary>
public interface ITrustPolicyService
{
    /// <summary>
    /// The channel's policy, or the shipped defaults when it has never been edited. Read-only and
    /// non-persisting: a channel that has not tuned anything gets defaults without a write on a read
    /// path (scoring runs on every chat message; it must not create rows).
    /// </summary>
    Task<TrustPolicy> GetAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The dashboard read: the same values plus whether this channel has pinned them or is still
    /// tracking the shipped defaults.
    /// </summary>
    Task<Result<TrustPolicyDto>> GetForEditingAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Save the channel's tuning, creating the row on first edit. Validates server-side before writing:
    /// the four weights must sum to 1.0, tier ceilings must ascend, and no multiplier, decay, penalty or
    /// heat value may be negative — a policy that cannot produce a sane score must never reach the
    /// scorer. Returns the saved policy.
    /// </summary>
    Task<Result<TrustPolicyDto>> UpdateAsync(
        Guid broadcasterId,
        UpdateTrustPolicyRequest request,
        CancellationToken cancellationToken = default
    );
}
