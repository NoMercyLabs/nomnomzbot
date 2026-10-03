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
