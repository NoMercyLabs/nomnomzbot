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
