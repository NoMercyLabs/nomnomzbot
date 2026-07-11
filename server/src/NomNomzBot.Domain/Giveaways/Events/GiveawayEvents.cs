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

namespace NomNomzBot.Domain.Giveaways.Events;

/// <summary>A giveaway opened for entries (giveaways.md §2) — drives announcements + the overlay widget.</summary>
public sealed class GiveawayOpenedEvent : DomainEventBase
{
    public required Guid GiveawayId { get; init; }
    public required string EntryMode { get; init; }
    public string? Keyword { get; init; }
}

/// <summary>Winners were drawn (giveaways.md §2) — one event per draw, carrying every winner.</summary>
public sealed class GiveawayDrawnEvent : DomainEventBase
{
    public required Guid GiveawayId { get; init; }
    public required IReadOnlyList<Guid> WinnerUserIds { get; init; }
    public required int EntryCount { get; init; }
    public required string PrizeMode { get; init; }
}
