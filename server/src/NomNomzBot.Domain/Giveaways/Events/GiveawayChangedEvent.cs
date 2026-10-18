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
