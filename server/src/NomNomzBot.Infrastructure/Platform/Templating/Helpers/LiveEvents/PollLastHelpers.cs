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
