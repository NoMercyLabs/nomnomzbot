// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using ChannelConfiguration = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Music;

public class MusicConfigService : IMusicConfigService
{
    private const string ConfigKey = "music:config";

    private readonly IApplicationDbContext _db;

    public MusicConfigService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Result<MusicConfigDto>> GetConfigAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        MusicConfigDto config = await LoadConfigAsync(broadcasterId, cancellationToken);
        return Result.Success(config);
    }

    public async Task<Result<MusicConfigDto>> UpdateConfigAsync(
        string broadcasterId,
        UpdateMusicConfigDto request,
        CancellationToken cancellationToken = default
    )
    {
        ChannelConfiguration? existing = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == ConfigKey,
            cancellationToken
        );

        MusicConfigData current = existing is not null
            ? JsonSerializer.Deserialize<MusicConfigData>(existing.Value ?? "{}")
                ?? new MusicConfigData()
            : new();

        if (request.IsEnabled.HasValue)
            current.IsEnabled = request.IsEnabled.Value;
        if (request.PreferredProvider is not null)
            current.PreferredProvider = request.PreferredProvider;
        if (request.MaxQueueSize.HasValue)
            current.MaxQueueSize = request.MaxQueueSize.Value;
        if (request.MaxRequestsPerUser.HasValue)
            current.MaxRequestsPerUser = request.MaxRequestsPerUser.Value;
        if (request.AllowYouTube.HasValue)
            current.AllowYouTube = request.AllowYouTube.Value;
        if (request.AllowSpotify.HasValue)
            current.AllowSpotify = request.AllowSpotify.Value;
        if (request.MinTrustLevel is not null)
            current.MinTrustLevel = request.MinTrustLevel;

        string json = JsonSerializer.Serialize(current);

        if (existing is not null)
        {
            existing.Value = json;
        }
        else
        {
            _db.Configurations.Add(
                new()
                {
                    BroadcasterId = broadcasterId,
                    Key = ConfigKey,
                    Value = json,
                }
            );
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(current));
    }

    private async Task<MusicConfigDto> LoadConfigAsync(
        string broadcasterId,
        CancellationToken cancellationToken
    )
    {
        ChannelConfiguration? entry = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == ConfigKey,
            cancellationToken
        );

        if (entry?.Value is null)
            return ToDto(new());

        MusicConfigData data =
            JsonSerializer.Deserialize<MusicConfigData>(entry.Value) ?? new MusicConfigData();
        return ToDto(data);
    }

    private static MusicConfigDto ToDto(MusicConfigData d) =>
        new(
            d.IsEnabled,
            d.PreferredProvider,
            d.MaxQueueSize,
            d.MaxRequestsPerUser,
            d.AllowYouTube,
            d.AllowSpotify,
            d.MinTrustLevel
        );

    private sealed class MusicConfigData
    {
        public bool IsEnabled { get; set; } = true;
        public string PreferredProvider { get; set; } = "auto";
        public int MaxQueueSize { get; set; } = 50;
        public int MaxRequestsPerUser { get; set; } = 5;
        public bool AllowYouTube { get; set; } = true;
        public bool AllowSpotify { get; set; } = true;
        public string MinTrustLevel { get; set; } = "everyone";
    }
}
