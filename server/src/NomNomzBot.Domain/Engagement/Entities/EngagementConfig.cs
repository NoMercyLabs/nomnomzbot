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
    public int GreetCooldownSeconds { get; set; } = EngagementDefaults.GreetCooldownSeconds;

    public int ConfigSchemaVersion { get; set; } = 1;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
