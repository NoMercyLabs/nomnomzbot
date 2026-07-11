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

namespace NomNomzBot.Domain.Giveaways.Entities;

/// <summary>
/// A giveaway campaign (giveaways.md G.6): open → collect entries → draw winners → fulfill. Two entry
/// modes (<c>keyword</c> typed in chat / <c>active_viewers</c> pulled from recent chatters), opt-in
/// eligibility filters, default-off sub-luck weighting, and four prize modes
/// (<c>announce</c> | <c>currency</c> | <c>pipeline</c> | <c>code_pool</c>). One giveaway per channel
/// may be active (open, or closed-but-undrawn) at a time.
/// </summary>
public class Giveaway : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    public string Title { get; set; } = null!;

    /// <summary><c>keyword</c> | <c>active_viewers</c> (D1).</summary>
    public string EntryMode { get; set; } = null!;

    /// <summary>The chat keyword viewers type while open (keyword mode only).</summary>
    public string? Keyword { get; set; }

    /// <summary>Loyalty-point cost per entry; null/0 = free.</summary>
    public long? EntryCost { get; set; }

    public int MaxEntriesPerUser { get; set; } = 1;

    /// <summary>Opt-in eligibility filters (D3), JSON; null/empty = everyone.</summary>
    public string? EligibilityJson { get; set; }

    /// <summary>Sub-luck ticket weighting (D4), JSON; null = 1 ticket each.</summary>
    public string? WeightingJson { get; set; }

    public int WinnerCount { get; set; } = 1;

    public bool ExcludeModerators { get; set; }

    /// <summary>Minutes a drawn winner has to claim before auto-forfeit (D7); null = no window.</summary>
    public int? ClaimWindowMinutes { get; set; }

    /// <summary><c>announce</c> | <c>currency</c> | <c>pipeline</c> | <c>code_pool</c> (D5).</summary>
    public string PrizeMode { get; set; } = null!;

    public long? PrizeCurrencyAmount { get; set; }

    /// <summary>Currency mode: pay the winner the summed entry costs instead of a fixed amount.</summary>
    public bool PrizeFromPot { get; set; }

    public Guid? PrizePipelineId { get; set; }

    public Guid? PrizeCodePoolId { get; set; }

    /// <summary><c>draft</c> | <c>open</c> | <c>closed</c> | <c>drawn</c> | <c>archived</c>.</summary>
    public string Status { get; set; } = GiveawayStatus.Draft;

    public DateTime? OpenedAt { get; set; }

    public DateTime? ClosesAt { get; set; }

    public DateTime? DrawnAt { get; set; }

    public int ConfigSchemaVersion { get; set; } = 1;
}

/// <summary>The <see cref="Giveaway.Status"/> lifecycle values.</summary>
public static class GiveawayStatus
{
    public const string Draft = "draft";
    public const string Open = "open";
    public const string Closed = "closed";
    public const string Drawn = "drawn";
    public const string Archived = "archived";
}

/// <summary>The <see cref="Giveaway.EntryMode"/> values (D1).</summary>
public static class GiveawayEntryMode
{
    public const string Keyword = "keyword";
    public const string ActiveViewers = "active_viewers";
}

/// <summary>The <see cref="Giveaway.PrizeMode"/> values (D5).</summary>
public static class GiveawayPrizeMode
{
    public const string Announce = "announce";
    public const string Currency = "currency";
    public const string Pipeline = "pipeline";
    public const string CodePool = "code_pool";
}
