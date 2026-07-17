// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Entities;

namespace NomNomzBot.Infrastructure.Obs.Transport;

/// <summary>
/// The per-channel transport selector (obs-control.md §3.2/§8): reads the channel's stored
/// <c>Mode</c> and routes to the direct socket (self-host) or the leader bridge (SaaS/remote). A
/// missing or disabled connection fails closed with <c>OBS_DISABLED</c> before any transport runs.
/// </summary>
public sealed class ObsTransportRouter : IObsTransport
{
    private readonly DirectObsTransport _direct;
    private readonly Bridge.BridgeObsTransport _bridge;
    private readonly IServiceScopeFactory _scopeFactory;

    public ObsTransportRouter(
        DirectObsTransport direct,
        Bridge.BridgeObsTransport bridge,
        IServiceScopeFactory scopeFactory
    )
    {
        _direct = direct;
        _bridge = bridge;
        _scopeFactory = scopeFactory;
    }

    public async Task<Result<ObsResponse>> SendAsync(
        Guid broadcasterId,
        Guid commandId,
        ObsRequest request,
        CancellationToken ct = default
    )
    {
        Result<IObsTransport> transport = await ResolveAsync(broadcasterId, ct);
        if (transport.IsFailure)
            return Result.Failure<ObsResponse>(transport.ErrorMessage!, transport.ErrorCode!);
        return await transport.Value.SendAsync(broadcasterId, commandId, request, ct);
    }

    public async Task<Result<IReadOnlyList<ObsResponse>>> SendBatchAsync(
        Guid broadcasterId,
        Guid commandId,
        ObsRequestBatch batch,
        CancellationToken ct = default
    )
    {
        Result<IObsTransport> transport = await ResolveAsync(broadcasterId, ct);
        if (transport.IsFailure)
            return Result.Failure<IReadOnlyList<ObsResponse>>(
                transport.ErrorMessage!,
                transport.ErrorCode!
            );
        return await transport.Value.SendBatchAsync(broadcasterId, commandId, batch, ct);
    }

    private async Task<Result<IObsTransport>> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ObsConnection? connection = await db.ObsConnections.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            ct
        );
        if (connection is null || !connection.IsEnabled)
            return Result.Failure<IObsTransport>(
                "OBS control is not enabled for this channel.",
                "OBS_DISABLED"
            );
        return Result.Success<IObsTransport>(connection.Mode == "bridge" ? _bridge : _direct);
    }
}
