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

using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>Is a prediction running, what it asks, its status and how long bets stay open.</summary>
internal sealed class PredictionStateHelpers()
    : PredictionHelperGroup(
        PredictionRow.Current("prediction.question", "Win?", (prediction, _) => prediction.Title),
        PredictionRow.State(
            "prediction.isActive",
            "true",
            (prediction, _) =>
                NumberText.Flag(prediction is not null && PredictionText.IsOpen(prediction))
        ),
        PredictionRow.Current(
            "prediction.status.now",
            "open",
            (prediction, _) => PredictionText.StatusWord(prediction)
        ),
        PredictionRow.Current(
            "prediction.timeLeft",
            "45s",
            (prediction, call) => PredictionText.TimeLeft(prediction, call.Scope.Now)
        )
    );
