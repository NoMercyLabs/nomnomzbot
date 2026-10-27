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

/// <summary>Where the running train is: level, progress against its goal, the bar and the time left.</summary>
internal sealed class HypeTrainStateHelpers()
    : LiveEventHelperGroup(
        "hypetrain",
        LiveHypeTrainRows.Flag("hypetrain.isActive", _ => true),
        LiveHypeTrainRows.Train("hypetrain.level", "3", (train, _) => LiveText.Plain(train.Level)),
        LiveHypeTrainRows.Train(
            "hypetrain.progress",
            "1,800",
            (train, call) => LiveText.Count(train.Progress, call.Culture)
        ),
        LiveHypeTrainRows.Train(
            "hypetrain.goal",
            "3,000",
            (train, call) => LiveText.Count(train.Goal, call.Culture)
        ),
        LiveHypeTrainRows.Train(
            "hypetrain.percent",
            "60%",
            (train, _) => LiveText.Percent(train.Progress, train.Goal)
        ),
        LiveHypeTrainRows.Train(
            "hypetrain.bar",
            "██████░░░░ 60%",
            (train, _) => LiveText.Bar(train.Progress, train.Goal)
        ),
        LiveHypeTrainRows.Train(
            "hypetrain.remaining",
            "1,200",
            (train, call) =>
                LiveText.Count(LiveText.Remaining(train.Progress, train.Goal), call.Culture)
        ),
        LiveHypeTrainRows.Train(
            "hypetrain.timeLeft",
            "4m 10s",
            (train, call) => LiveText.TimeLeft(train.ExpiresAt, call.Scope.Now)
        ),
        LiveHypeTrainRows.Train(
            "hypetrain.total",
            "5,400",
            (train, call) => LiveText.Count(train.Total, call.Culture)
        )
    );
