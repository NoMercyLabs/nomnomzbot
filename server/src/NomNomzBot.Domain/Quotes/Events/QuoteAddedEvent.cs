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

namespace NomNomzBot.Domain.Quotes.Events;

/// <summary>
/// Published after a quote is added (quotes.md §2). Carries the assigned per-channel number and the
/// author so projections / overlays can react. <see cref="DomainEventBase.BroadcasterId"/> is the tenant.
/// </summary>
public sealed class QuoteAddedEvent : DomainEventBase
{
    /// <summary>The id of the quote.</summary>
    public required Guid QuoteId { get; init; }

    /// <summary>The number of the quote, as viewers see it in chat.</summary>
    public required int Number { get; init; }

    /// <summary>The id of the user who added the quote. Empty when it is not known.</summary>
    public Guid? CreatedByUserId { get; init; }
}
