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

/// <summary>The newest poll that has finished, from the channel's poll and prediction history.</summary>
internal sealed class PollLastHelpers()
    : PollHelperGroup(
        PollRow.Last(
            "poll.last.winner",
            "chips",
            last => last.Options.FirstOrDefault(o => o.IsWinner)?.Title ?? string.Empty
        ),
        PollRow.Last("poll.last.question", "Best snack?", last => last.Title)
    );
