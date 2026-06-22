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
/// A derived per-stream attendance window (schema M.2, APPEND-ONLY). Opened on a viewer's first activity inside a
/// live window, extended on each subsequent activity, closed at stream offline. <see cref="PresenceConfirmed"/>
/// flips once there are ≥2 activity events ≥60s apart — the anti-AFK basis the economy's watch-time earning
/// consumes. Watch-time is demonstrated presence, never a lurker heuristic.
/// </summary>
public class WatchSession : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public Guid ViewerProfileId { get; set; }
    public Guid ViewerUserId { get; set; }

    /// <summary>The covering stream's id (Twitch stream id string — matches <c>Stream.Id</c>), null if unattributed.</summary>
    public string? StreamId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public long DurationSeconds { get; set; }
    public bool PresenceConfirmed { get; set; }
    public int MessageCountInSession { get; set; }
    public DateTime CreatedAt { get; set; }
}
