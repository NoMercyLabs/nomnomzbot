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

/// <summary>The options of the running poll: all of them, or one by its number.</summary>
internal sealed class PollOptionHelpers()
    : PollHelperGroup(
        PollRow.Active("poll.options", "chips, cookies", (poll, _) => PollText.Titles(poll)),
        PollRow.Active(
            "poll.options.numbered",
            "1. chips 2. cookies",
            (poll, _) => PollText.Numbered(poll)
        ),
        PollRow.Active(
            "poll.results.bars",
            "chips ██████░░░░ 60% · cookies ████░░░░░░ 40%",
            (poll, _) => PollText.Bars(poll)
        ),
        PollRow.Active(
            "poll.option",
            "cookies",
            (poll, call) => PollText.WithOption(call, poll, (choice, _) => choice.Title),
            args: 1
        ),
        PollRow.Active(
            "poll.option.votes",
            "8",
            (poll, call) => PollText.WithOption(call, poll, (choice, _) => choice.Votes.ToString()),
            args: 1
        ),
        PollRow.Active(
            "poll.option.percent",
            "40%",
            (poll, call) => PollText.WithOption(call, poll, PollText.Share),
            args: 1
        )
    );
