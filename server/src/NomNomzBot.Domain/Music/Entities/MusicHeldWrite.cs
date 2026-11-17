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

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Music.Entities;

/// <summary>
/// A favorite or banger write the music write queue is holding until the stream ends. The row is
/// inserted when the write is held and deleted when the provider accepts it, so a restart cannot drop
/// it. Rows are a plain outbox: no soft delete, a sent write leaves nothing to keep. Nav-free; the
/// tenant is <see cref="BroadcasterId"/>.
/// </summary>
public class MusicHeldWrite : ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The channel whose provider connection makes the write.</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>The provider key, for example <c>spotify</c> or <c>youtube</c>.</summary>
    [MaxLength(30)]
    public string Provider { get; set; } = null!;

    /// <summary>The write kind by name: <c>Save</c>, <c>Unsave</c> or <c>PlaylistAdd</c>.</summary>
    [MaxLength(30)]
    public string Kind { get; set; } = null!;

    /// <summary>The tracks to write, as a JSON array of URI strings.</summary>
    public string TrackUrisJson { get; set; } = "[]";

    /// <summary>The target playlist, for a playlist add only.</summary>
    [MaxLength(200)]
    public string? PlaylistId { get; set; }

    /// <summary>When the write was held; the flush sends a channel's rows oldest first.</summary>
    public DateTime CreatedAt { get; set; }
}
