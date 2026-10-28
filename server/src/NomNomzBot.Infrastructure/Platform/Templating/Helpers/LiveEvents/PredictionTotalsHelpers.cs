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

/// <summary>Who leads the running prediction, the pot, the players and the top player. Nobody leads before the first bet.</summary>
internal sealed class PredictionTotalsHelpers()
    : PredictionHelperGroup(
        PredictionRow.Current(
            "prediction.leading",
            "Yes",
            (prediction, _) => PredictionText.Leading(prediction)?.Title ?? string.Empty
        ),
        PredictionRow.Current(
            "prediction.leading.points",
            "3,000",
            (prediction, call) =>
                PredictionText.Leading(prediction) is { } leading
                    ? PredictionText.Points(leading.ChannelPoints, call.Culture)
                    : string.Empty
        ),
        PredictionRow.Current(
            "prediction.leading.percent",
            "75%",
            (prediction, _) =>
                PredictionText.Leading(prediction) is { } leading
                    ? PredictionText.Share(leading, prediction)
                    : string.Empty
        ),
        PredictionRow.Current(
            "prediction.points.total",
            "4,000",
            (prediction, call) =>
                PredictionText.Points(PredictionText.TotalPoints(prediction), call.Culture)
        ),
        PredictionRow.Current(
            "prediction.users.total",
            "12",
            (prediction, call) =>
                PredictionText.Points(PredictionText.TotalUsers(prediction), call.Culture)
        ),
        PredictionRow.Current(
            "prediction.topPredictor",
            "ViewerOne (1,000)",
            (prediction, call) => PredictionText.TopPredictor(prediction, call.Culture)
        )
    );
