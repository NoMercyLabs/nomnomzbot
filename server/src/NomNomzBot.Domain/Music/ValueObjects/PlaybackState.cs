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

/// <summary>
/// The director's state for one channel. <see cref="Item"/> is the queue item, or null for the owner's
/// own music. <see cref="Reason"/> is set for <see cref="PlaybackStateKind.Blocked"/>.
/// </summary>
public sealed record PlaybackState(
    PlaybackStateKind Kind,
    EngineKind? Engine = null,
    PlaybackItem? Item = null,
    string? Reason = null
)
{
    public static PlaybackState Idle { get; } = new(PlaybackStateKind.Idle);

    public static PlaybackState Starting(EngineKind engine, PlaybackItem item) =>
        new(PlaybackStateKind.Starting, engine, item);

    public static PlaybackState Playing(EngineKind engine, PlaybackItem? item) =>
        new(PlaybackStateKind.Playing, engine, item);

    public static PlaybackState PausedByOwner(EngineKind engine, PlaybackItem? item) =>
        new(PlaybackStateKind.PausedByOwner, engine, item);

    public static PlaybackState Blocked(string reason) =>
        new(PlaybackStateKind.Blocked, Reason: reason);
}
