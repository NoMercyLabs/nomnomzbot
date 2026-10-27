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

/// <summary>Who leads the running poll, by how much, and whether it is a tie. Nobody leads before the first vote.</summary>
internal sealed class PollLeadingHelpers()
    : PollHelperGroup(
        PollRow.Active(
            "poll.leading",
            "chips",
            (poll, _) => PollText.Leading(poll)?.Title ?? string.Empty
        ),
        PollRow.Active(
            "poll.leading.votes",
            "12",
            (poll, _) => PollText.Leading(poll)?.Votes.ToString() ?? string.Empty
        ),
        PollRow.Active(
            "poll.leading.percent",
            "60%",
            (poll, _) =>
                PollText.Leading(poll) is { } leading ? PollText.Share(leading, poll) : string.Empty
        ),
        PollRow.Active("poll.votes.total", "20", (poll, _) => PollText.TotalVotes(poll).ToString()),
        PollRow.State(
            "poll.isTie",
            "false",
            (poll, _) => NumberText.Flag(poll is not null && PollText.IsTie(poll))
        )
    );
