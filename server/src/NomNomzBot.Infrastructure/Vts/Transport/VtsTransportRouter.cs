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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Domain.Vts.Entities;

namespace NomNomzBot.Infrastructure.Vts.Transport;

/// <summary>
/// Per-channel VTS transport selector (vtube-studio.md §6 — Mode-on-row, matching the built OBS
/// router): direct → the server-held socket; bridge → the shared relay leader. A missing or
/// disabled connection fails closed <c>VTS_DISABLED</c> before any transport runs.
/// </summary>
public sealed class VtsTransportRouter : IVtsTransport
{
    private readonly DirectVtsTransport _direct;
    private readonly BridgeVtsTransport _bridge;
    private readonly IServiceScopeFactory _scopeFactory;

    public VtsTransportRouter(
        DirectVtsTransport direct,
        BridgeVtsTransport bridge,
        IServiceScopeFactory scopeFactory
    )
    {
        _direct = direct;
        _bridge = bridge;
        _scopeFactory = scopeFactory;
    }

    public async Task<Result<string>> RequestAsync(
        Guid broadcasterId,
        string requestType,
        string? dataJson,
        CancellationToken ct = default
    )
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        VtsConnection? connection = await db.VtsConnections.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            ct
        );
        if (connection is null || !connection.IsEnabled)
            return Result.Failure<string>(
                "VTube Studio control is not enabled for this channel.",
                "VTS_DISABLED"
            );
        IVtsTransport transport = connection.Mode == "bridge" ? _bridge : _direct;
        return await transport.RequestAsync(broadcasterId, requestType, dataJson, ct);
    }
}
