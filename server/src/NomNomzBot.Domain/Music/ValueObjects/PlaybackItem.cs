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
