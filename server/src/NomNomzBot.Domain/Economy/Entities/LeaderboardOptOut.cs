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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// A viewer's choice to be excluded from this channel's leaderboards (economy.md L.2) — the GDPR opt-out. While
/// it exists (not soft-deleted), the viewer is filtered out of every live ranking and snapshot. One per
/// <c>(BroadcasterId, ViewerUserId)</c>; re-including a viewer (opt-in) soft-deletes the row.
/// </summary>
public class LeaderboardOptOut : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid ViewerUserId { get; set; }
    public string ViewerTwitchUserId { get; set; } = null!;
    public DateTime OptedOutAt { get; set; }
}
