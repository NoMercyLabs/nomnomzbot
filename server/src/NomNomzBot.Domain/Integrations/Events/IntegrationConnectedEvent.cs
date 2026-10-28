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

namespace NomNomzBot.Domain.Integrations.Events;

/// <summary>
/// Published when an external integration (twitch / spotify / discord / youtube …) connects for the first
/// time (identity-auth §2). Tenant-scoped connections carry the channel in <c>BroadcasterId</c>; platform
/// connections (the shared bot) leave it at <see cref="System.Guid.Empty"/>.
/// </summary>
public sealed class IntegrationConnectedEvent : DomainEventBase
{
    /// <summary>The internal id of the integration connection.</summary>
    public required Guid ConnectionId { get; init; }

    /// <summary>The service that was connected, as a lower case key, for example twitch, spotify, discord or youtube.</summary>
    public required string Provider { get; init; }

    /// <summary>The account id at that service (as text).</summary>
    public required string ProviderAccountId { get; init; }
}
