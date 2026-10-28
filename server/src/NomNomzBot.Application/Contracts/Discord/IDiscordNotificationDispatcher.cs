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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Discord;

/// <summary>
/// Dispatch + dedupe (discord.md §3.4, Infrastructure-internal, no controller). The go-live/trigger path:
/// gate on both-opt-in, atomically dedupe via the unique <c>(NotificationConfigId, DedupeKey)</c> insert,
/// render the template+embed, post via <c>IDiscordBotGateway</c>, persist the outcome on the appended row, and
/// publish <c>DiscordNotificationDispatchedEvent</c>. Never throws for a Discord-side failure (captured as
/// <c>Status=failed</c>).
/// </summary>
public interface IDiscordNotificationDispatcher
{
    /// <summary>The core trigger path for the tenant's enabled config of the given trigger. Returns the outcome.</summary>
    Task<Result<DiscordDispatchOutcomeDto>> DispatchAsync(
        DiscordDispatchRequest request,
        CancellationToken ct = default
    );

    /// <summary>Append-only dispatch history for a connection (paged), newest first. Read-only.</summary>
    Task<Result<PagedList<DiscordDispatchLogDto>>> GetDispatchLogAsync(
        Guid broadcasterId,
        Guid connectionId,
        int page,
        int pageSize,
        CancellationToken ct = default
    );
}
