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

namespace NomNomzBot.Domain.Stream.Entities;

/// <summary>
/// One broadcaster's own custom shoutout line for a SPECIFIC target — old-bot parity (the legacy bot's
/// <c>Shoutout</c> row, keyed by (channel, shouted user)): a personal note the broadcaster writes for
/// someone they shout out, independent of whether that target has ever connected to NomNomzBot or set
/// their own <see cref="Identity.Entities.Channel.ShoutoutTemplate"/>. Takes priority over the target's own
/// template (ShoutoutAction's lookup order), since it is the broadcaster's own deliberate choice about how
/// THEY specifically want to introduce this person — never editable by anyone but this broadcaster.
/// </summary>
public class ShoutoutOverride : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>The shouted-out user's Twitch id (not a local User row — most targets never chat here).</summary>
    [MaxLength(50)]
    public string TargetTwitchUserId { get; set; } = null!;

    /// <summary>Display name at the time this was saved — shown in the dashboard list without a lookup.</summary>
    [MaxLength(50)]
    public string TargetDisplayName { get; set; } = null!;

    [MaxLength(1000)]
    public string MessageTemplate { get; set; } = null!;
}
