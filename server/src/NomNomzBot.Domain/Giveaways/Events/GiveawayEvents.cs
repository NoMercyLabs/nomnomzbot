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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Giveaways.Events;

/// <summary>A giveaway opened for entries (giveaways.md §2) — drives announcements + the overlay widget.</summary>
public sealed class GiveawayOpenedEvent : DomainEventBase
{
    /// <summary>The id of the giveaway.</summary>
    public required Guid GiveawayId { get; init; }

    /// <summary>How viewers enter: keyword or active_viewers.</summary>
    public required string EntryMode { get; init; }

    /// <summary>The chat word that enters a viewer. Empty when the entry mode uses no keyword.</summary>
    public string? Keyword { get; init; }
}

/// <summary>Winners were drawn (giveaways.md §2) — one event per draw, carrying every winner.</summary>
public sealed class GiveawayDrawnEvent : DomainEventBase
{
    /// <summary>The id of the giveaway.</summary>
    public required Guid GiveawayId { get; init; }

    /// <summary>The ids of the winning viewers.</summary>
    public required IReadOnlyList<Guid> WinnerUserIds { get; init; }

    /// <summary>How many entries were in the draw.</summary>
    public required int EntryCount { get; init; }

    /// <summary>How the prize is given out: announce, currency, pipeline or code_pool.</summary>
    public required string PrizeMode { get; init; }
}
