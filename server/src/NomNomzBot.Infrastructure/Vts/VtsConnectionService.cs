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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Domain.Vts.Entities;

namespace NomNomzBot.Infrastructure.Vts;

/// <summary>
/// VTS connection configuration over the P.19 row (vtube-studio.md §3). The plugin token — granted
/// once by the streamer approving the in-VTS popup — is sealed through the token vault (provider
/// <c>vts</c>, field <c>plugin_token</c>) and can only be OPENED by the transport; no API path ever
/// returns it. Upserts never touch the token (the authorize flow owns it); storing a new token flips
/// <c>Status</c> to <c>authorized</c>.
/// </summary>
public class VtsConnectionService : IVtsConnectionService
{
    private const string SecretProvider = "vts";
    private const string SecretField = "plugin_token";

    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;

    public VtsConnectionService(IApplicationDbContext db, ITokenProtector tokenProtector)
    {
        _db = db;
        _tokenProtector = tokenProtector;
    }

    public async Task<Result<VtsConnectionDto>> GetAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        VtsConnection? connection = await FindAsync(broadcasterId, ct);
        return Result.Success(ToDto(connection ?? new VtsConnection()));
    }

    public async Task<Result<VtsConnectionDto>> UpsertAsync(
        Guid broadcasterId,
        UpsertVtsConnectionRequest request,
        CancellationToken ct = default
    )
    {
        VtsConnection? connection = await FindAsync(broadcasterId, ct);
        if (connection is null)
        {
            connection = new VtsConnection { BroadcasterId = broadcasterId };
            _db.VtsConnections.Add(connection);
        }

        connection.Mode = request.Mode;
        connection.Endpoint = string.IsNullOrWhiteSpace(request.Endpoint)
            ? "ws://localhost:8001"
            : request.Endpoint.Trim();
        connection.IsEnabled = request.IsEnabled;
        if (request.EventSubscriptionsMask is int mask)
            connection.EventSubscriptionsMask = mask;

        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(connection));
    }

    public async Task<Result<VtsConnectionDto>> RotateBridgeTokenAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        VtsConnection? connection = await FindAsync(broadcasterId, ct);
        if (connection is null)
            return Errors.NotFound<VtsConnectionDto>("VTS connection", broadcasterId.ToString());

        connection.BridgeToken = Guid.NewGuid().ToString();
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(connection));
    }

    public async Task<string?> GetPluginTokenForTransportAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        VtsConnection? connection = await FindAsync(broadcasterId, ct);
        if (connection?.PluginTokenCipher is null)
            return null;
        return await _tokenProtector.TryUnprotectAsync(
            connection.PluginTokenCipher,
            Context(broadcasterId),
            ct
        );
    }

    public async Task<Result> StorePluginTokenAsync(
        Guid broadcasterId,
        string pluginToken,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(pluginToken))
            return Errors.ValidationFailed("An empty plugin token cannot be stored.");

        VtsConnection? connection = await FindAsync(broadcasterId, ct);
        if (connection is null)
        {
            connection = new VtsConnection { BroadcasterId = broadcasterId };
            _db.VtsConnections.Add(connection);
        }

        connection.PluginTokenCipher = await _tokenProtector.ProtectAsync(
            pluginToken,
            Context(broadcasterId),
            ct
        );
        connection.Status = "authorized";
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<VtsConnection?> FindAsync(Guid broadcasterId, CancellationToken ct) =>
        await _db.VtsConnections.FirstOrDefaultAsync(c => c.BroadcasterId == broadcasterId, ct);

    private static TokenProtectionContext Context(Guid broadcasterId) =>
        new(broadcasterId.ToString(), SecretProvider, SecretField);

    private static VtsConnectionDto ToDto(VtsConnection c) =>
        new(
            c.Mode,
            c.Endpoint,
            HasPluginToken: c.PluginTokenCipher is not null,
            HasBridgeToken: c.BridgeToken is not null,
            c.EventSubscriptionsMask,
            c.IsEnabled,
            c.Status,
            c.LastConnectedAt
        );
}
