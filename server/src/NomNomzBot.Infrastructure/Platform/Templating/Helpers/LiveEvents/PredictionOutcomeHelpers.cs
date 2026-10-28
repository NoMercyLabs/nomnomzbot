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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The outcomes of the running prediction: all titles, or one outcome by its number.</summary>
internal sealed class PredictionOutcomeHelpers()
    : PredictionHelperGroup(
        PredictionRow.Current(
            "prediction.outcomes.list",
            "Yes, No",
            (prediction, _) => PredictionText.Titles(prediction)
        ),
        PredictionRow.Current(
            "prediction.outcome",
            "No",
            (prediction, call) =>
                PredictionText.WithOutcome(call, prediction, (outcome, _) => outcome.Title),
            args: 1
        ),
        PredictionRow.Current(
            "prediction.outcome.points",
            "1,000",
            (prediction, call) =>
                PredictionText.WithOutcome(
                    call,
                    prediction,
                    (outcome, _) => PredictionText.Points(outcome.ChannelPoints, call.Culture)
                ),
            args: 1
        ),
        PredictionRow.Current(
            "prediction.outcome.percent",
            "25%",
            (prediction, call) =>
                PredictionText.WithOutcome(call, prediction, PredictionText.Share),
            args: 1
        ),
        PredictionRow.Current(
            "prediction.outcome.odds",
            "1:4",
            (prediction, call) => PredictionText.WithOutcome(call, prediction, PredictionText.Odds),
            args: 1
        )
    );
