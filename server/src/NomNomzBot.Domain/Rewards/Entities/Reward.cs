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

namespace NomNomzBot.Domain.Rewards.Entities;

public class Reward : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    [MaxLength(255)]
    public string Title { get; set; } = null!;

    [MaxLength(2000)]
    public string? Response { get; set; }

    [MaxLength(20)]
    public string Permission { get; set; } = "everyone";

    public bool IsEnabled { get; set; } = true;

    [MaxLength(500)]
    public string? Description { get; set; }

    public string? PipelineJson { get; set; }

    public bool IsPlatform { get; set; }

    /// <summary>
    /// True when this reward was created under the bot's own Twitch <c>client_id</c> and can therefore be
    /// updated, deleted, and have its redemptions fulfilled/refunded via Helix. Rewards created in the Twitch
    /// UI or by another app (e.g. StreamElements) are read-only to us — Twitch reports them with
    /// <c>is_manageable = false</c>, and they are imported with this flag <c>false</c>. Converting such a reward
    /// to bot-controlled means recreating an equivalent reward under our client (a new row with this flag true),
    /// since Twitch does not allow taking over the original.
    /// </summary>
    public bool IsManageable { get; set; }

    /// <summary>Twitch's own reward ID (UUID). Null until synced with Twitch.</summary>
    [MaxLength(50)]
    public string? TwitchRewardId { get; set; }

    public int? Cost { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
