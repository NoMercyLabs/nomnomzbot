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

namespace NomNomzBot.Domain.Quotes.Events;

/// <summary>
/// Published after a quote is added (quotes.md §2). Carries the assigned per-channel number and the
/// author so projections / overlays can react. <see cref="DomainEventBase.BroadcasterId"/> is the tenant.
/// </summary>
public sealed class QuoteAddedEvent : DomainEventBase
{
    public required Guid QuoteId { get; init; }
    public required int Number { get; init; }
    public Guid? CreatedByUserId { get; init; }
}
