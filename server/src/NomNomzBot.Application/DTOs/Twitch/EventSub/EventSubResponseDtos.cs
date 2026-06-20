// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.DTOs.Twitch.EventSub;

/// <summary>One registry row projected to the controller surface (twitch-eventsub §4.4).</summary>
public sealed record EventSubSubscriptionDto(
    Guid Id,
    string EventType,
    string Version,
    string Transport,
    string Status,
    bool Enabled,
    int? Cost,
    string? TwitchSubscriptionId,
    string? LastError,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt
);

/// <summary>The outcome of a registry reconcile pass (twitch-eventsub §4.4).</summary>
public sealed record EventSubReconcileReportDto(
    int Created,
    int Revoked,
    int Repaired,
    int Unchanged,
    IReadOnlyList<string> Errors
);

/// <summary>The POST body for creating one subscription (twitch-eventsub §4.4).</summary>
public sealed record CreateEventSubSubscriptionRequest(string EventType);

/// <summary>The result of dispatching one notification through the journal + bus (twitch-eventsub §4.4).</summary>
public sealed record NotificationDispatchResult(
    Guid EventId,
    long StreamPosition,
    bool WasDuplicate
);
