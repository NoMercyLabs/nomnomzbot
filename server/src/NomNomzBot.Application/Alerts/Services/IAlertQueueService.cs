// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Alerts.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Alerts.Services;

/// <summary>
/// The one alert queue across every platform connection (widgets-overlays.md §1.2). Every event that qualifies
/// as an on-air alert — a follow, a sub, a raid, a supporter tip from ANY provider — is enqueued here first,
/// independently of whether the streamer has ever installed a widget or attached an OBS browser source to the
/// auto-provisioned alert system surface. Delivery is attempted opportunistically (a live overlay connection
/// receives the push immediately), but the row itself is the durable, queryable proof the alert happened.
/// </summary>
public interface IAlertQueueService
{
    /// <summary>
    /// Writes one alert to <paramref name="broadcasterId"/>'s queue and attempts immediate delivery to the
    /// channel's alert system surface. Never throws on "nobody is listening" — that outcome is recorded as
    /// <c>AlertQueueStatus.Queued</c>, never reported as delivered.
    /// </summary>
    Task<Result<AlertQueueEntryDto>> EnqueueAsync(
        Guid broadcasterId,
        string provider,
        string kind,
        object payload,
        CancellationToken cancellationToken = default
    );

    /// <summary>The channel's queue, most recent first, plus whether the alert surface is live right now.</summary>
    Task<Result<AlertQueueDto>> GetQueueAsync(
        Guid broadcasterId,
        int limit = 50,
        CancellationToken cancellationToken = default
    );
}
