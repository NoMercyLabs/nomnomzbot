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
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Infrastructure.Platform.Configuration;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Integrations;

/// <summary>
/// The dashboard-facing write/read/clear surface over a channel's own Spotify BYOC credentials — the
/// counterpart to <see cref="ChannelCredentialsResolver"/>'s read path. Writes to the same channel-scoped
/// <c>Configuration</c> rows the resolver reads (<c>"spotify.client_id"</c> plain / <c>"spotify.client_secret"</c>
/// sealed under the resolver's channel AAD), so a value saved here is the value the live OAuth flows resolve.
/// </summary>
public sealed class ChannelSpotifyCredentialsService(
    IApplicationDbContext db,
    ITokenProtector protector
) : IChannelSpotifyCredentialsService
{
    private const string Provider = "spotify";
    private const string ClientIdKey = $"{Provider}.client_id";
    private const string ClientSecretKey = $"{Provider}.client_secret";

    public async Task<Result<ChannelSpotifyCredentialsDto>> GetAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        List<ConfigEntity> rows = await LoadRowsAsync(channelId, cancellationToken);
        string? clientId = rows.FirstOrDefault(r => r.Key == ClientIdKey)?.Value;
        bool hasSecret =
            rows.FirstOrDefault(r => r.Key == ClientSecretKey)?.SecureValue is not null;
        return Result.Success(new ChannelSpotifyCredentialsDto(clientId, hasSecret));
    }

    public async Task<Result<ChannelSpotifyCredentialsDto>> SetAsync(
        Guid channelId,
        SetChannelSpotifyCredentialsDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
            return Errors
                .ValidationFailed("A Spotify client id is required.")
                .ToTyped<ChannelSpotifyCredentialsDto>();
        if (string.IsNullOrWhiteSpace(request.ClientSecret))
            return Errors
                .ValidationFailed("A Spotify client secret is required.")
                .ToTyped<ChannelSpotifyCredentialsDto>();

        List<ConfigEntity> rows = await LoadRowsAsync(channelId, cancellationToken);

        ConfigEntity? idRow = rows.FirstOrDefault(r => r.Key == ClientIdKey);
        if (idRow is null)
        {
            idRow = new() { BroadcasterId = channelId, Key = ClientIdKey };
            db.Configurations.Add(idRow);
        }
        idRow.Value = request.ClientId.Trim();

        ConfigEntity? secretRow = rows.FirstOrDefault(r => r.Key == ClientSecretKey);
        if (secretRow is null)
        {
            secretRow = new() { BroadcasterId = channelId, Key = ClientSecretKey };
            db.Configurations.Add(secretRow);
        }
        secretRow.SecureValue = await protector.ProtectAsync(
            request.ClientSecret,
            ChannelCredentialsResolver.ContextFor(channelId, Provider),
            cancellationToken
        );

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(new ChannelSpotifyCredentialsDto(idRow.Value, true));
    }

    public async Task<Result<ChannelSpotifyCredentialsDto>> ClearAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        List<ConfigEntity> rows = await LoadRowsAsync(channelId, cancellationToken);
        if (rows.Count == 0)
            return Result.Success(new ChannelSpotifyCredentialsDto(null, false));

        db.Configurations.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(new ChannelSpotifyCredentialsDto(null, false));
    }

    private async Task<List<ConfigEntity>> LoadRowsAsync(
        Guid channelId,
        CancellationToken cancellationToken
    ) =>
        await db
            .Configurations.Where(c =>
                c.BroadcasterId == channelId && (c.Key == ClientIdKey || c.Key == ClientSecretKey)
            )
            .ToListAsync(cancellationToken);
}
