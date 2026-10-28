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

/// <summary>Published when an external integration disconnects (identity-auth §2).</summary>
public sealed class IntegrationDisconnectedEvent : DomainEventBase
{
    /// <summary>The internal id of the integration connection that was removed.</summary>
    public required Guid ConnectionId { get; init; }

    /// <summary>The service that was disconnected, as a lower case key, for example twitch, spotify, discord or youtube.</summary>
    public required string Provider { get; init; }

    /// <summary>Why the connection ended.</summary>
    public required string Reason { get; init; }
}
