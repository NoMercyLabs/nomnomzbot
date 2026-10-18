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

/// <summary>One queued song, in the one shape both engines share.</summary>
/// <param name="Id">Queue row id. Unique per row; the director removes by this id only.</param>
/// <param name="OwnerKey">The fair-queue owner (the requester).</param>
/// <param name="Engine">The engine that plays this item.</param>
/// <param name="Uri">The track id or URI on that engine.</param>
public sealed record PlaybackItem(
    string Id,
    string OwnerKey,
    EngineKind Engine,
    string Uri,
    string Title,
    IReadOnlyList<string> Artists,
    string? AlbumArtUrl,
    int DurationMs,
    string? RequestedBy
);
