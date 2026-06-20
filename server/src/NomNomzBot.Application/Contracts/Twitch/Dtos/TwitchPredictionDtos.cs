// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Predictions" category wire models (GET/POST/PATCH /predictions). These records deserialize
// straight from Twitch's snake_case JSON via the transport's naming policy — no per-property
// annotations. Twitch ids stay strings; timestamps are DateTimeOffset. The owning tenant is always a
// Guid method argument and is injected into the request body internally — it never appears in a DTO the
// caller fills (CreatePredictionRequest / EndPredictionRequest carry no broadcaster).

/// <summary>One of the 2–10 possible outcomes of a prediction, with its running tally of predictors.</summary>
public sealed record TwitchPredictionOutcome(
    string Id,
    string Title,
    int Users,
    int ChannelPoints,
    IReadOnlyList<TwitchPredictionTopPredictor>? TopPredictors,
    string Color
);

/// <summary>A leading predictor on an outcome — points wagered and (once resolved) points won.</summary>
public sealed record TwitchPredictionTopPredictor(
    string UserId,
    string UserName,
    string UserLogin,
    int ChannelPointsUsed,
    int ChannelPointsWon
);

/// <summary>
/// A Channel Points Prediction. <see cref="WinningOutcomeId"/> is empty until the prediction resolves;
/// <see cref="EndedAt"/> and <see cref="LockedAt"/> are null until those transitions occur. Returned by
/// Get / Create / End Prediction.
/// </summary>
public sealed record TwitchPrediction(
    string Id,
    string BroadcasterId,
    string BroadcasterName,
    string BroadcasterLogin,
    string Title,
    string? WinningOutcomeId,
    IReadOnlyList<TwitchPredictionOutcome> Outcomes,
    int PredictionWindow,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? LockedAt
);

/// <summary>One possible outcome supplied when creating a prediction — only the title is set by the caller.</summary>
public sealed record CreatePredictionOutcome(string Title);

/// <summary>
/// Create Prediction request: title, the 2–10 outcomes, and the betting window in seconds. The broadcaster
/// is the Guid method argument (injected into the wire body internally), not part of this body.
/// </summary>
public sealed record CreatePredictionRequest(
    string Title,
    IReadOnlyList<CreatePredictionOutcome> Outcomes,
    int PredictionWindow
);
