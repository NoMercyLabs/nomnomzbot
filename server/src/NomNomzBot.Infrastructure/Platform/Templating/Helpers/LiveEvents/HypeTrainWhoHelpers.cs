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
