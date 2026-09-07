// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Music.ValueObjects;

/// <summary>
/// One accepted song request, as stored on the generic <c>Record</c> store (<c>RecordType</c> =
/// <see cref="RecordType"/>, <c>UserId</c> = the requester's platform id). One row per request: a repeat is
/// a second row, which is what makes "most-requested" a countable fact rather than a coin toss between
/// everything a viewer has ever asked for.
///
/// <para>
/// The resolved metadata is stored alongside the uri deliberately. The legacy bot kept only a bare song id,
/// so reading a viewer's top track back meant asking the provider what that id even was — a network round
/// trip on a celebration path, and nothing at all once a track had been delisted. Here the answer is already
/// in the row, and the uri is only needed when something is actually going to be queued.
/// </para>
///
/// <para>
/// One shape shared by the writer and every reader, so the two can never drift: a field the writer stops
/// setting is a compile error at the reader rather than a silently empty card.
/// </para>
/// </summary>
/// <param name="TrackUri">Provider uri (e.g. <c>spotify:track:...</c>) — what gets re-queued.</param>
/// <param name="TrackName">Resolved track title at the time of the request.</param>
/// <param name="Artist">Resolved artist at the time of the request.</param>
/// <param name="ImageUrl">Resolved artwork at the time of the request, when the provider gave one.</param>
/// <param name="Provider">Which source resolved it — <c>spotify</c> / <c>youtube</c>.</param>
/// <param name="SourceEventId">
/// The journal <c>EventId</c> this row was reconstructed from, when it was reconstructed rather than written
/// live. It is what makes a backfill re-runnable: a second pass skips every source event it has already
/// turned into a row, so nobody's favourite is double-counted by running the job twice. <c>null</c> on a row
/// written live, which has no source event to point at.
/// </param>
public sealed record SongRequestHistory(
    string TrackUri,
    string TrackName,
    string Artist,
    string? ImageUrl,
    string Provider,
    string? SourceEventId = null
)
{
    /// <summary>The <c>Record.RecordType</c> discriminator every song-request history row carries.</summary>
    public const string RecordType = "SongRequest";
}
