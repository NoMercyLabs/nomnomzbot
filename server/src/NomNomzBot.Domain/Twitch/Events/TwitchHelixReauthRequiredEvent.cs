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

namespace NomNomzBot.Domain.Twitch.Events;

/// <summary>
/// Raised when a Helix call returns 401 and the single refresh-and-retry also fails, or when a required
/// scope is absent (twitch-helix.md §2). Drives <c>IntegrationConnections.Status = needs_reauth</c>. The
/// inherited <c>BroadcasterId</c> is the owning channel, or <c>Guid.Empty</c> for app/bot-token calls.
/// </summary>
public sealed class TwitchHelixReauthRequiredEvent : DomainEventBase
{
    /// <summary>The provider — always <c>twitch</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>The service whose token needs re-auth — <c>twitch</c> or <c>twitch_bot</c>.</summary>
    public required string ServiceName { get; init; }

    /// <summary>Why re-auth is needed — <c>unauthorized</c> | <c>missing_scope</c> | <c>token_revoked</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>The absent scope (e.g. <c>channel:read:subscriptions</c>); null unless <see cref="Reason"/> is <c>missing_scope</c>.</summary>
    public string? MissingScope { get; init; }
}
