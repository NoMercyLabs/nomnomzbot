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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
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
    ITokenProtector protector,
    ISystemCredentialsProvider systemCredentials
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
        return Result.Success(
            new ChannelSpotifyCredentialsDto(
                clientId,
                hasSecret,
                await HasSharedAppAsync(cancellationToken)
            )
        );
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
        return Result.Success(
            new ChannelSpotifyCredentialsDto(
                idRow.Value,
                true,
                await HasSharedAppAsync(cancellationToken)
            )
        );
    }

    public async Task<Result<ChannelSpotifyCredentialsDto>> ClearAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        List<ConfigEntity> rows = await LoadRowsAsync(channelId, cancellationToken);
        if (rows.Count > 0)
        {
            db.Configurations.RemoveRange(rows);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(
            new ChannelSpotifyCredentialsDto(
                null,
                false,
                await HasSharedAppAsync(cancellationToken)
            )
        );
    }

    /// <summary>True only when a channel without its own app would fall back to a configured platform app —
    /// the same rule <see cref="ChannelCredentialsResolver"/> applies when it resolves credentials.</summary>
    private async Task<bool> HasSharedAppAsync(CancellationToken cancellationToken) =>
        ChannelCredentialsResolver.FallsBackToSharedApp(Provider)
        && await systemCredentials.GetAsync(Provider, cancellationToken) is not null;

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
