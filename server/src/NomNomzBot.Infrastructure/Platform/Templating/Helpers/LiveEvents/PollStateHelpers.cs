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
