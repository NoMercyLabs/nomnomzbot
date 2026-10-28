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

namespace NomNomzBot.Domain.Analytics.Entities;

/// <summary>
/// Per-viewer daily message-count aggregate (schema M.4) — counts only, never content (data minimization). One
/// upserted row per <c>(channel, viewer, channel-local date)</c>, folded from chat-message events.
/// </summary>
public class MessageActivityDaily : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public Guid ViewerProfileId { get; set; }
    public Guid ViewerUserId { get; set; }
    public DateOnly ActivityDate { get; set; }
    public int MessageCount { get; set; }
    public DateTime? FirstMessageAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
}
