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

namespace NomNomzBot.Domain.Billing.Events;

/// <summary>
/// An operator changed platform billing: authored or edited a tier, minted or revoked an invite code,
/// granted a tier, the founders badge or a comp, refunded an invoice, or priced a usage unit. Platform-scoped,
/// so the inherited <c>BroadcasterId</c> stays <c>Guid.Empty</c>; the channel a change touched travels in
/// <see cref="TargetBroadcasterId"/>. The operator hub pushes it so every open billing desk reloads.
/// </summary>
public sealed class BillingChangedEvent : DomainEventBase
{
    /// <summary><c>tier</c> | <c>invite</c> | <c>tier-grant</c> | <c>founder</c> | <c>grant</c> | <c>invoice</c> | <c>priced-unit</c>.</summary>
    public required string Area { get; init; }

    /// <summary><c>created</c> | <c>updated</c> | <c>revoked</c> | <c>granted</c> | <c>refunded</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The id of the tier, invite code, grant, invoice or priced unit the change landed on.</summary>
    public required Guid SubjectId { get; init; }

    /// <summary>The channel the change touched, when it touched one.</summary>
    public Guid? TargetBroadcasterId { get; init; }
}
