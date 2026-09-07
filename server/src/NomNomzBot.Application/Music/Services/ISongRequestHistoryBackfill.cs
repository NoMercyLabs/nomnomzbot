// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// Rebuilds a channel's per-viewer song-request history from its own event journal, for the period before the
/// bot recorded that history live.
///
/// <para>
/// Explicitly invoked, never driven in the background. The 2026-08-27 replay incident set the rule this obeys:
/// a replay is completely silent — it makes no outbound call of any kind — and it never runs by itself. Both
/// halves matter here. Silence is why a reconstructed row carries the track uri but no title or artwork:
/// naming a track means asking Spotify, and a backfill is not allowed to. The metadata is filled in afterwards
/// by a separate, equally explicit step.
/// </para>
///
/// <para>
/// Being manual is also what keeps the counts honest. The live writer already records every new request, so a
/// job that kept re-reading the same events would count each future request twice — once live, once replayed.
/// </para>
/// </summary>
public interface ISongRequestHistoryBackfill
{
    Task<Result<SongRequestBackfillSummary>> RunAsync(
        Guid broadcasterId,
        SongRequestBackfillOptions options,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// What counts as a song request in this channel's history. Both are channel-specific: the reward could be
/// called anything, and the command is whatever the streamer bound it to.
/// </summary>
/// <param name="RewardTitles">Channel-point reward titles that were song requests (case-insensitive).</param>
/// <param name="CommandNames">Chat commands that were song requests, leading sigil included (<c>!sr</c>).</param>
/// <param name="DryRun">Count and report without writing a single row — the preview behind the button.</param>
public sealed record SongRequestBackfillOptions(
    IReadOnlyCollection<string> RewardTitles,
    IReadOnlyCollection<string> CommandNames,
    bool DryRun = false
);

/// <summary>
/// The honest account of one run. <paramref name="UnresolvableFreeText"/> is reported rather than hidden
/// because it is the one thing a replay genuinely cannot recover: a request typed as a search phrase only
/// became a track after the old bot asked the provider, and that answer was never journaled.
/// </summary>
public sealed record SongRequestBackfillSummary(
    int EventsScanned,
    int RequestsFound,
    int RowsWritten,
    int AlreadyPresent,
    int UnresolvableFreeText
);
