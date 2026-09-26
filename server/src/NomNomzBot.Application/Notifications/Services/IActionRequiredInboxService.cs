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
/// The dashboard's "action required" notification centre (S071a, plan item A0): the union of every registered
/// <see cref="IActionRequiredSource"/>, minus the items the channel has dismissed. Each source derives its items
/// from state its subsystem already persists, so the inbox never shows a condition that is not really there.
/// </summary>
public interface IActionRequiredInboxService
{
    Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists a dismissal for each given stable item id (S-OWN22 T2) so <see cref="GetItemsAsync"/> stops
    /// surfacing it. A grouped <c>held-user:{sourceUserId}</c> id is expanded into one dismissal row per
    /// contained <c>held:{queueItemGuid}</c> key; already-dismissed keys are skipped (idempotent). Returns
    /// the number of dismissal rows written.
    /// </summary>
    Task<Result<int>> DismissAsync(
        Guid channelId,
        Guid dismissedByUserId,
        List<string> ids,
        CancellationToken cancellationToken = default
    );
}
