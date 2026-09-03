// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

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
}
