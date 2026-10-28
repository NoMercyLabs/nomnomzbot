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

/// <summary>
/// A giveaway changed: it was created, edited, deleted, opened, closed, entered, drawn or redrawn. One event
/// per change, so every open giveaway page reloads its list, entries and winners without polling.
/// </summary>
public sealed class GiveawayChangedEvent : DomainEventBase
{
    /// <summary>The id of the giveaway.</summary>
    public required Guid GiveawayId { get; init; }

    /// <summary>
    /// <c>created</c> | <c>updated</c> | <c>deleted</c> | <c>opened</c> | <c>closed</c> | <c>entered</c> |
    /// <c>drawn</c> | <c>redrawn</c>.
    /// </summary>
    public required string Change { get; init; }
}
