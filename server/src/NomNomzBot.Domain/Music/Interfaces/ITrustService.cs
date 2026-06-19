// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// Computes and manages a per-user trust score using Bamo's exponential decay algorithm.
///
/// Score components:
///   - requestScore    : based on how many times the user has been in queue
///   - accountScore    : based on Twitch account age
///   - contentScore    : based on message quality (no violations)
///   - popularityScore : based on follower count / watch time
///
/// Modifiers:
///   - followage &lt; 7 days: × 0.75 penalty
///   - reputation boost: gap between current score and 1.0 is halved each time
///   - violations: score is reduced by configured penalty amount
/// </summary>
public interface ITrustService
{
    /// <summary>Gets the current trust score for a user in a channel (0.0 – 1.0).</summary>
    Task<double> GetScoreAsync(string broadcasterId, string userId, CancellationToken ct = default);

    /// <summary>Records a violation and reduces the trust score.</summary>
    Task RecordViolationAsync(
        string broadcasterId,
        string userId,
        string violationType,
        CancellationToken ct = default
    );

    /// <summary>Boosts the trust score towards 1.0 (gap-halving on each positive interaction).</summary>
    Task RecordPositiveInteractionAsync(
        string broadcasterId,
        string userId,
        CancellationToken ct = default
    );
}

public sealed class TrustScore
{
    public required string UserId { get; init; }
    public required string BroadcasterId { get; init; }
    public required double Score { get; init; }
    public required double RequestScore { get; init; }
    public required double AccountScore { get; init; }
    public required double ContentScore { get; init; }
    public required double PopularityScore { get; init; }
    public DateTime ComputedAt { get; init; } = DateTime.UtcNow;
}
