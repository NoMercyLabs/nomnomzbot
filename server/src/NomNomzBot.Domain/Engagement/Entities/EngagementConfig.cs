// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Engagement.Entities;

/// <summary>
/// Per-channel engagement-trigger configuration (engagement.md G.11). Every trigger is OFF by default
/// (the default-deny rule) — the streamer opts in per moment. One row per channel.
/// </summary>
public class EngagementConfig : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }

    public Guid BroadcasterId { get; set; }

    /// <summary>Fire on a viewer's first-ever message in this channel (while live).</summary>
    public bool FirstTimeChatterEnabled { get; set; }

    /// <summary>Fire on a viewer's first message this stream, having chatted before.</summary>
    public bool ReturningChatterEnabled { get; set; }

    /// <summary>Fire when a viewer hits a configured consecutive-stream milestone.</summary>
    public bool WatchStreakEnabled { get; set; }

    /// <summary>
    /// JSON <c>int[]</c> of streak milestones (e.g. <c>[5,10,25,50,100]</c>). Null/empty = every stream.
    /// </summary>
    public string? StreakMilestonesJson { get; set; }

    /// <summary>Per-channel burst limiter for greetings (D4). Default 5s.</summary>
    public int GreetCooldownSeconds { get; set; } = 5;

    public int ConfigSchemaVersion { get; set; } = 1;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
