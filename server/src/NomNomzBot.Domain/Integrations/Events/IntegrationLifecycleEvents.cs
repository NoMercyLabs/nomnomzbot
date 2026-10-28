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

/// <summary>Published whenever an integration's tokens are (re)stored — connect, refresh (identity-auth §2).</summary>
public sealed class IntegrationTokenRefreshedEvent : DomainEventBase
{
    /// <summary>The internal id of the integration connection.</summary>
    public required Guid ConnectionId { get; init; }

    /// <summary>The service whose access was renewed, as a lower case key, for example twitch, spotify, discord or youtube.</summary>
    public required string Provider { get; init; }

    /// <summary>When the renewed access ends, in UTC time.</summary>
    public required DateTime ExpiresAt { get; init; }
}

/// <summary>
/// Published when an integration's refresh failures cross the threshold and the connection needs the user
/// to re-authorize (identity-auth §2) — drives the reconnect prompt and backoff.
/// </summary>
public sealed class IntegrationNeedsReauthEvent : DomainEventBase
{
    /// <summary>The internal id of the integration connection.</summary>
    public required Guid ConnectionId { get; init; }

    /// <summary>The service that needs the streamer to sign in again.</summary>
    public required string Provider { get; init; }

    /// <summary>How many renewals in a row have failed.</summary>
    public required int ConsecutiveFailureCount { get; init; }
}

/// <summary>
/// Published when reconciliation finds the provider granted FEWER scopes than before (identity-auth §3.4a):
/// the still-valid token simply lost a scope. Handlers disable the features that depended on the dropped
/// scopes and surface a "reconnect to restore X" prompt. Distinct from needs-reauth (token unusable).
/// </summary>
public sealed class ScopesDroppedEvent : DomainEventBase
{
    /// <summary>The service that took permissions away.</summary>
    public required string Provider { get; init; }

    /// <summary>The permissions the service no longer grants.</summary>
    public required IReadOnlyList<string> DroppedScopes { get; init; }

    /// <summary>The bot features that were switched off because of the lost permissions.</summary>
    public required IReadOnlyList<string> DisabledFeatures { get; init; }
}
