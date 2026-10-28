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
