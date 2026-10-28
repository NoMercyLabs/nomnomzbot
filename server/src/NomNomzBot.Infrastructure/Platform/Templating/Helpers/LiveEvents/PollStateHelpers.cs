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

/// <summary>Is a poll running, what it asks, and how long it has left.</summary>
internal sealed class PollStateHelpers()
    : PollHelperGroup(
        PollRow.Active("poll.question", "Best snack?", (poll, _) => poll.Title),
        PollRow.State("poll.isActive", "true", (poll, _) => NumberText.Flag(poll is not null)),
        PollRow.Active(
            "poll.timeLeft",
            "1m 30s",
            (poll, call) => PollText.TimeLeft(poll, call.Scope.Now)
        ),
        PollRow.Active(
            "poll.endsAt",
            "15:32",
            (poll, call) => PollText.EndsAtLocal(poll, call.Scope.Timezone)
        )
    );
