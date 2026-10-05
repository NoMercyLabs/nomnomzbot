// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Stream.RaidSuggestions;

/// <summary>One live channel waiting to be scored, and whether the broadcaster follows it.</summary>
internal sealed record RaidCandidate(TwitchStream Stream, bool IsFollowed);

/// <summary>One scoring step: its point change and the label shown in the reason text.</summary>
internal sealed record RaidScoreReason(int Delta, string Label);

/// <summary>A scored raid target. <see cref="Reason"/> is the legacy text, e.g. "+60 Software cat, +25 small (30v)".</summary>
internal sealed record RaidScoreResult(
    TwitchStream Stream,
    bool IsFollowed,
    int Score,
    IReadOnlyList<RaidScoreReason> Reasons,
    string Reason
);
