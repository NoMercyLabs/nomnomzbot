// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
