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
