// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
