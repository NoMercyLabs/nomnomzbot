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
/// A track a channel has banned from song requests (the legacy <c>!bansong</c>). The song-request
/// admission path refuses a blocked track with a typed reason before it ever reaches the fair queue.
/// The <see cref="Title"/> is a display snapshot taken at block time — the block itself matches on
/// <see cref="TrackUri"/>. Unblocking soft-deletes the row; re-blocking inserts a fresh one (the
/// unique index is filtered on live rows). Nav-free (convention-mapped) — the tenant is
/// <see cref="BroadcasterId"/>.
/// </summary>
public class BlockedTrack : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The owning channel (tenant key).</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>The provider key the track lives on (e.g. <c>spotify</c>, <c>youtube</c>).</summary>
    [MaxLength(30)]
    public string Provider { get; set; } = null!;

    /// <summary>The provider track URI/id the admission path matches requests against.</summary>
    [MaxLength(500)]
    public string TrackUri { get; set; } = null!;

    /// <summary>Display snapshot of the track title at block time (shown in the dashboard list).</summary>
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    /// <summary>Optional human reason recorded when the track was blocked.</summary>
    [MaxLength(300)]
    public string? Reason { get; set; }

    /// <summary>The Twitch user id of whoever blocked the track, when known.</summary>
    [MaxLength(50)]
    public string? BlockedByUserId { get; set; }
}
