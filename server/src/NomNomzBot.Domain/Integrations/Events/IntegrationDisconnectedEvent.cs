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

namespace NomNomzBot.Domain.Integrations.Events;

/// <summary>Published when an external integration disconnects (identity-auth §2).</summary>
public sealed class IntegrationDisconnectedEvent : DomainEventBase
{
    public required Guid ConnectionId { get; init; }
    public required string Provider { get; init; }
    public required string Reason { get; init; }
}
