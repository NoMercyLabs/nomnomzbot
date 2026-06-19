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
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// A channel timer that sends a message at a configured interval,
/// with optional minimum chat activity enforcement.
/// </summary>
public class Timer : SoftDeletableEntity, ITenantScoped
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string BroadcasterId { get; set; } = null!;

    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>List of messages to rotate through (round-robin).</summary>
    public List<string> Messages { get; set; } = [];

    /// <summary>How often the timer fires (in minutes).</summary>
    public int IntervalMinutes { get; set; } = 30;

    /// <summary>Minimum number of chat messages since last fire before the timer will fire again.</summary>
    public int MinChatActivity { get; set; } = 0;

    public bool IsEnabled { get; set; } = true;

    /// <summary>UTC time the timer last sent a message.</summary>
    public DateTime? LastFiredAt { get; set; }

    /// <summary>Round-robin index into Messages.</summary>
    public int NextMessageIndex { get; set; } = 0;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
