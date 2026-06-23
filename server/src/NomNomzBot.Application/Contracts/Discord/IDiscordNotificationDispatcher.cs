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
