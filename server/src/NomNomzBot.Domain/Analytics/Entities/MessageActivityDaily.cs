// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
