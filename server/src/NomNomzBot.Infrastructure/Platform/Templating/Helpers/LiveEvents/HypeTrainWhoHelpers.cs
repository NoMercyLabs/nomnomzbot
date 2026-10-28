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

/// <summary>Who and what the train is: its biggest contributor, its kind and the all-time record.</summary>
internal sealed class HypeTrainWhoHelpers()
    : LiveEventHelperGroup(
        "hypetrain",
        LiveHypeTrainRows.Train(
            "hypetrain.topContributor",
            "ViewerOne",
            (train, _) =>
                train.TopContributions.MaxBy(c => c.Total) is { } top ? top.UserName : null
        ),
        LiveHypeTrainRows.Record(
            "hypetrain.record.level",
            "5",
            record => LiveText.Plain(record.Level)
        ),
        LiveHypeTrainRows.Flag("hypetrain.isGolden", train => train.Type == "golden_kappa"),
        LiveHypeTrainRows.Flag("hypetrain.isShared", train => train.IsSharedTrain)
    );
