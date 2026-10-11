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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// What the bot knew about a raider the moment their raid arrived: how old the account is, whether the
/// channel raided with them before, and whether they are banned here or blocked across the network.
/// One row per incoming raid; written once and never edited.
/// </summary>
public class RaidAssessment : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>The Twitch id of the channel that sent the raid.</summary>
    [MaxLength(50)]
    public string RaiderTwitchUserId { get; set; } = null!;

    [MaxLength(50)]
    public string RaiderLogin { get; set; } = null!;

    [MaxLength(100)]
    public string RaiderDisplayName { get; set; } = null!;

    public int ViewerCount { get; set; }

    /// <summary>When the raider's Twitch account was created. Null when the Twitch lookup failed.</summary>
    public DateTime? AccountCreatedAt { get; set; }

    /// <summary>How many raids from this raider this channel had received before this one.</summary>
    public int EarlierRaidCount { get; set; }

    /// <summary>The raider is currently banned in this channel.</summary>
    public bool IsBanned { get; set; }

    /// <summary>The raider is under an active or partial network block.</summary>
    public bool IsNetworkBlocked { get; set; }

    /// <summary>One of <see cref="RaidVerdicts"/>.</summary>
    [MaxLength(20)]
    public string Verdict { get; set; } = RaidVerdicts.Unknown;

    /// <summary>When the raid arrived.</summary>
    public DateTime OccurredAt { get; set; }
}

/// <summary>The closed set of raid verdicts.</summary>
public static class RaidVerdicts
{
    /// <summary>The channel raided with this raider before and nothing is against them.</summary>
    public const string Known = "known";

    /// <summary>First contact: no history and no block.</summary>
    public const string Unknown = "unknown";

    /// <summary>Banned here or blocked across the network.</summary>
    public const string Hostile = "hostile";
}
