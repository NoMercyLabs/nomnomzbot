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

/// <summary>
/// Published when an external integration (twitch / spotify / discord / youtube …) connects for the first
/// time (identity-auth §2). Tenant-scoped connections carry the channel in <c>BroadcasterId</c>; platform
/// connections (the shared bot) leave it at <see cref="System.Guid.Empty"/>.
/// </summary>
public sealed class IntegrationConnectedEvent : DomainEventBase
{
    public required Guid ConnectionId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderAccountId { get; init; }
}
