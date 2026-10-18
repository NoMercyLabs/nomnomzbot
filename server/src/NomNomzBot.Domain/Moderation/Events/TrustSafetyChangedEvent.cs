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

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>
/// An operator changed the platform trust and safety desk: confirmed or overturned a reviewed automatic
/// action, or applied or lifted a network-wide block. Platform-scoped, so the inherited
/// <c>BroadcasterId</c> stays <c>Guid.Empty</c>. The operator hub pushes it so every open desk reloads.
/// </summary>
public sealed class TrustSafetyChangedEvent : DomainEventBase
{
    /// <summary><c>review</c> | <c>network-block</c>.</summary>
    public required string Area { get; init; }

    /// <summary><c>confirmed</c> | <c>overturned</c> | <c>applied</c> | <c>lifted</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The detection id for a review, the block id for a network block.</summary>
    public required Guid SubjectId { get; init; }
}
