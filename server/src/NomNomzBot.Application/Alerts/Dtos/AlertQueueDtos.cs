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

namespace NomNomzBot.Application.Alerts.Dtos;

/// <summary>One row of the channel's single cross-platform alert queue, as read by the dashboard.</summary>
public sealed record AlertQueueEntryDto(
    Guid Id,
    string Provider,
    string Kind,
    string PayloadJson,
    string Status,
    DateTime CreatedAt,
    DateTime? DeliveredAt
);

/// <summary>
/// The alert queue's read model plus honest connection state (widgets-overlays.md §1.2, the
/// overlay-only-output presence-check law): <see cref="OverlayConnected"/> is never inferred from
/// <see cref="Entries"/> containing delivered rows — it is read live from
/// <c>IOverlayPresenceRegistry</c> at the moment of the call, so the dashboard can show "not connected"
/// even when the queue itself is healthy.
/// </summary>
public sealed record AlertQueueDto(
    bool OverlayConnected,
    IReadOnlyList<AlertQueueEntryDto> Entries
);
