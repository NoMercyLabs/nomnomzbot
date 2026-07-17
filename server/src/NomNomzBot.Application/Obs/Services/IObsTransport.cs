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
using NomNomzBot.Application.Obs.Dtos;

namespace NomNomzBot.Application.Obs.Services;

/// <summary>
/// The deployment-profile OBS transport (obs-control.md §3.2): direct (self-host — the server holds
/// the OBS-WS v5 socket) or bridge (SaaS — the leader browser-source executes). Idempotent per
/// command id; failures are Result codes (<c>OBS_NOT_CONNECTED</c>, <c>OBS_BRIDGE_OFFLINE</c>, the
/// OBS error comment), never throws.
/// </summary>
public interface IObsTransport
{
    Task<Result<ObsResponse>> SendAsync(
        Guid broadcasterId,
        Guid commandId,
        ObsRequest request,
        CancellationToken ct = default
    );

    Task<Result<IReadOnlyList<ObsResponse>>> SendBatchAsync(
        Guid broadcasterId,
        Guid commandId,
        ObsRequestBatch batch,
        CancellationToken ct = default
    );
}
