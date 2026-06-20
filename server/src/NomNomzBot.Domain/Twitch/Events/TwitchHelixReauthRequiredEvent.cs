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
