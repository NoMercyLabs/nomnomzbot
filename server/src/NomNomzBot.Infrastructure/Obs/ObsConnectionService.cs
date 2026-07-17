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
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Entities;

namespace NomNomzBot.Infrastructure.Obs;

/// <summary>
/// OBS connection configuration over the P.14 row (obs-control.md §3). The OBS-WS password is sealed
/// through the token vault (per-subject DEK, AAD-bound to <c>obs/password</c> — obs-control.md §1
/// names the AEAD custody; as built it rides <see cref="ITokenProtector"/>, the platform's
/// secret-custody facade over that cipher) and can only be OPENED by the direct transport — no API
/// path ever returns it. The bridge token is minted lazily on first setup ask and rotates by
/// replacement.
/// </summary>
public class ObsConnectionService : IObsConnectionService
{
    private const string SecretProvider = "obs";
    private const string SecretField = "password";

    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;

    public ObsConnectionService(IApplicationDbContext db, ITokenProtector tokenProtector)
    {
        _db = db;
        _tokenProtector = tokenProtector;
    }

    public async Task<Result<ObsConnectionDto>> GetAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        ObsConnection? connection = await FindAsync(broadcasterId, ct);
        return Result.Success(ToDto(connection ?? new ObsConnection()));
    }

    public async Task<Result<ObsConnectionDto>> UpsertAsync(
        Guid broadcasterId,
        UpsertObsConnectionRequest request,
        CancellationToken ct = default
    )
    {
        ObsConnection? connection = await FindAsync(broadcasterId, ct);
        if (connection is null)
        {
            connection = new ObsConnection { BroadcasterId = broadcasterId };
            _db.ObsConnections.Add(connection);
        }

        connection.Mode = request.Mode;
        connection.Host = string.IsNullOrWhiteSpace(request.Host)
            ? "127.0.0.1"
            : request.Host.Trim();
        connection.Port = request.Port ?? 4455;
        connection.IsEnabled = request.IsEnabled;
        if (request.EventSubscriptionsMask is int mask)
            connection.EventSubscriptionsMask = mask;

        // Write-only password semantics: null keeps the stored secret, "" clears it, anything else re-seals.
        if (request.Password is not null)
        {
            connection.PasswordCipher =
                request.Password.Length == 0
                    ? null
                    : await _tokenProtector.ProtectAsync(
                        request.Password,
                        Context(broadcasterId),
                        ct
                    );
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(connection));
    }

    public async Task<Result<ObsBridgeSetupDto>> RotateBridgeTokenAsync(
        Guid broadcasterId,
        string backendUrl,
        CancellationToken ct = default
    )
    {
        ObsConnection? connection = await FindAsync(broadcasterId, ct);
        if (connection is null)
            return Errors.NotFound<ObsBridgeSetupDto>("OBS connection", broadcasterId.ToString());

        connection.BridgeToken = Guid.NewGuid().ToString();
        await _db.SaveChangesAsync(ct);
        return Result.Success(BuildSetup(connection, backendUrl));
    }

    public async Task<Result<ObsBridgeSetupDto>> GetBridgeSetupAsync(
        Guid broadcasterId,
        string backendUrl,
        CancellationToken ct = default
    )
    {
        ObsConnection? connection = await FindAsync(broadcasterId, ct);
        if (connection is null)
        {
            connection = new ObsConnection { BroadcasterId = broadcasterId, Mode = "bridge" };
            _db.ObsConnections.Add(connection);
        }
        if (connection.BridgeToken is null)
        {
            connection.BridgeToken = Guid.NewGuid().ToString();
            await _db.SaveChangesAsync(ct);
        }
        return Result.Success(BuildSetup(connection, backendUrl));
    }

    public async Task<string?> GetPasswordForTransportAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        ObsConnection? connection = await FindAsync(broadcasterId, ct);
        if (connection?.PasswordCipher is null)
            return null;
        return await _tokenProtector.TryUnprotectAsync(
            connection.PasswordCipher,
            Context(broadcasterId),
            ct
        );
    }

    private async Task<ObsConnection?> FindAsync(Guid broadcasterId, CancellationToken ct) =>
        await _db.ObsConnections.FirstOrDefaultAsync(c => c.BroadcasterId == broadcasterId, ct);

    private static TokenProtectionContext Context(Guid broadcasterId) =>
        new(broadcasterId.ToString(), SecretProvider, SecretField);

    private static ObsBridgeSetupDto BuildSetup(ObsConnection connection, string backendUrl) =>
        new($"{backendUrl.TrimEnd('/')}/obs-bridge?token={connection.BridgeToken}");

    private static ObsConnectionDto ToDto(ObsConnection c) =>
        new(
            c.Mode,
            c.Host,
            c.Port,
            HasPassword: c.PasswordCipher is not null,
            HasBridgeToken: c.BridgeToken is not null,
            c.EventSubscriptionsMask,
            c.IsEnabled,
            c.LastConnectedAt,
            c.LastError
        );
}
