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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The Helix "Predictions" category sub-client: read, create, and lock/resolve/cancel Channel Points
/// Predictions (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// Every method takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id internally
/// (the invariant: a Guid never reaches Twitch). Each returns <see cref="Result"/>/<see cref="Result{T}"/>
/// carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchPredictionsApi
{
    /// <summary>
    /// Get Predictions — one page of the channel's predictions, newest first. Optionally filtered to specific
    /// prediction ids. Requires <c>channel:read:predictions</c>.
    /// </summary>
    Task<Result<TwitchPage<TwitchPrediction>>> GetPredictionsAsync(
        Guid broadcasterId,
        IReadOnlyList<string>? predictionIds,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>
    /// Create Prediction — opens a new prediction with 2–10 outcomes and a betting window. Returns the created
    /// prediction. Requires <c>channel:manage:predictions</c>.
    /// </summary>
    Task<Result<TwitchPrediction>> CreatePredictionAsync(
        Guid broadcasterId,
        CreatePredictionRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// End Prediction — locks, resolves (with the winning outcome), or cancels a prediction. <paramref name="status"/>
    /// is <c>RESOLVED</c> | <c>CANCELED</c> | <c>LOCKED</c>; <paramref name="winningOutcomeId"/> is required for
    /// <c>RESOLVED</c>. Returns the updated prediction. Requires <c>channel:manage:predictions</c>.
    /// </summary>
    Task<Result<TwitchPrediction>> EndPredictionAsync(
        Guid broadcasterId,
        string predictionId,
        string status,
        string? winningOutcomeId,
        CancellationToken ct = default
    );
}
