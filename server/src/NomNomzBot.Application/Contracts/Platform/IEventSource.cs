// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Enums;

namespace NomNomzBot.Application.Contracts.Platform;

/// <summary>
/// The cross-platform inbound-event seam (twitch-rebuild doc, twitch-eventsub §3.1). Twitch is the single
/// implemented source today; the seam is provider-agnostic so another provider is an additive implementation,
/// not a seam change.
/// </summary>
public interface IEventSource
{
    /// <summary>The provider discriminator — the Twitch impl returns <c>twitch</c>.</summary>
    string Provider { get; }

    /// <summary>
    /// Declaratively reconciles the channel's subscription set to exactly <paramref name="eventTypes"/>
    /// (creates missing, leaves existing, no-ops duplicates); persists registry rows. Returns failure with
    /// <c>SCOPE_MISSING</c> / <c>SERVICE_UNAVAILABLE</c> on Twitch rejection.
    /// </summary>
    Task<Result> EnsureSubscribedAsync(
        Guid broadcasterId,
        IReadOnlyCollection<string> eventTypes,
        CancellationToken ct = default
    );

    /// <summary>
    /// Revokes every active subscription for the tenant at Twitch and soft-deletes its registry rows
    /// (channel offboarding / erasure).
    /// </summary>
    Task<Result> UnsubscribeAllAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>A synchronous transport-health snapshot for the dashboard / health endpoint.</summary>
    EventSourceHealth Health { get; }
}

/// <summary>The transport-health snapshot surfaced by <see cref="IEventSource.Health"/>.</summary>
public sealed record EventSourceHealth(
    bool IsConnected,
    EventSubTransportKind Transport,
    int ActiveSubscriptions,
    DateTimeOffset? LastEventAt,
    DateTimeOffset? LastReconnectAt
);
