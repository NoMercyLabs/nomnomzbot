// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Engagement.Entities;

/// <summary>
/// The small per-viewer state the engagement subsystem owns to detect its moments (engagement.md G.12).
/// Self-contained — it does NOT read analytics on the chat hot path (D3). One row per channel+viewer.
/// The stream-session ids are the string <c>Stream.Id</c> (Twitch stream id / platform session id);
/// <c>null</c> means offline.
/// </summary>
public class ViewerEngagementState : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    public Guid ViewerUserId { get; set; }

    /// <summary>PII-hash of the viewer's platform id (never the raw login).</summary>
    [MaxLength(64)]
    public string ViewerTwitchUserId { get; set; } = null!;

    public DateTime FirstChatAt { get; set; }

    public DateTime LastChatAt { get; set; }

    /// <summary>The stream session the viewer last chatted in (D3 streak anchor).</summary>
    [MaxLength(50)]
    public string? LastSeenStreamSessionId { get; set; }

    /// <summary>The stream session the viewer was last greeted in (D4 dedup).</summary>
    [MaxLength(50)]
    public string? LastGreetedStreamSessionId { get; set; }

    /// <summary>Consecutive streams attended (D3).</summary>
    public int ConsecutiveStreams { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(ViewerUserId))]
    public virtual User Viewer { get; set; } = null!;
}
