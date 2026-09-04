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
/// One broadcaster's own custom line for a SPECIFIC person — old-bot parity (the legacy bot's
/// <c>Shoutout</c> row, keyed by (channel, target)): a personal note the broadcaster writes for someone,
/// independent of whether that target has ever connected to NomNomzBot or set their own
/// <see cref="Identity.Entities.Channel.ShoutoutTemplate"/>. Takes priority over the target's own template
/// (ShoutoutAction's lookup order), since it is the broadcaster's own deliberate choice about how THEY
/// specifically want to introduce this person — never editable by anyone but this broadcaster.
///
/// <para><see cref="Kind"/> says WHICH line: the shoutout, or the message used when raiding them. They are
/// the same fact about the same person — "here is what I say about you" — so they share one row shape and
/// one editor rather than growing a second parallel table that would inevitably drift.</para>
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

    /// <summary>
    /// Which line this is: <see cref="ShoutoutOverrideKinds.Shoutout"/> (the default, and what every row
    /// written before this column existed is) or <see cref="ShoutoutOverrideKinds.Raid"/>.
    /// </summary>
    [MaxLength(20)]
    public string Kind { get; set; } = ShoutoutOverrideKinds.Shoutout;
}

/// <summary>The closed set of per-person message kinds. A row's kind is one of exactly these.</summary>
public static class ShoutoutOverrideKinds
{
    /// <summary>The line posted when this person is shouted out.</summary>
    public const string Shoutout = "shoutout";

    /// <summary>The line posted when this channel raids this person.</summary>
    public const string Raid = "raid";

    public static bool IsKnown(string? kind) => kind is Shoutout or Raid;

    public static IReadOnlyList<string> All { get; } = [Shoutout, Raid];
}
