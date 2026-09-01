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
using NomNomzBot.Application.Notifications.Dtos;

namespace NomNomzBot.Application.Notifications.Services;

/// <summary>
/// Aggregates the dashboard's "action required" notification centre (S071a) from existing, already-tracked
/// signals — never a fabricated or hardcoded list. This slice covers the two categories with a genuine,
/// already-persisted backing signal today: dead/expired integration connections
/// (<c>IntegrationConnection.Status</c> = <c>needs_reauth</c>/<c>expired</c>, written by
/// <c>IIntegrationTokenVault.MarkRefreshFailureAsync</c>) and AutoMod-held chat messages pending review
/// (<c>ModerationQueueItem</c>, source=AutoMod, status=pending). Missing OAuth scopes, failed timer runs, and
/// pending unban requests are deliberately NOT included: the scope-diagnostics matrix documents a missing
/// progressive scope as feature-gated (not an error), timers carry no run-failure signal today, and unban
/// requests are read live from Twitch under an operator token this channel-scoped aggregation does not carry —
/// surfacing any of those honestly would require new tracking, which is out of scope for this slice.
/// </summary>
public interface IActionRequiredInboxService
{
    Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    );
}
