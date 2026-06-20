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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform;

public class FeatureService : IFeatureService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public FeatureService(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<Result<List<FeatureStatusDto>>> GetFeaturesAsync(
        string channelId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return Result.Success(new List<FeatureStatusDto>());

        List<FeatureStatusDto> features = await _db
            .ChannelFeatures.Where(f => f.BroadcasterId == broadcasterId)
            .Select(f => new FeatureStatusDto(
                f.FeatureKey,
                f.IsEnabled,
                f.EnabledAt,
                f.RequiredScopes
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(features);
    }

    public async Task<Result<FeatureStatusDto>> ToggleFeatureAsync(
        string channelId,
        string featureKey,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return Result.Failure<FeatureStatusDto>(
                $"Invalid channel id '{channelId}'.",
                "INVALID_CHANNEL_ID"
            );

        ChannelFeature? feature = await _db.ChannelFeatures.FirstOrDefaultAsync(
            f => f.BroadcasterId == broadcasterId && f.FeatureKey == featureKey,
            cancellationToken
        );

        if (feature is null)
        {
            feature = new()
            {
                BroadcasterId = broadcasterId,
                FeatureKey = featureKey,
                IsEnabled = true,
                EnabledAt = _timeProvider.GetUtcNow().UtcDateTime,
            };
            _db.ChannelFeatures.Add(feature);
        }
        else
        {
            feature.IsEnabled = !feature.IsEnabled;
            feature.EnabledAt = feature.IsEnabled ? _timeProvider.GetUtcNow().UtcDateTime : null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(
            new FeatureStatusDto(
                feature.FeatureKey,
                feature.IsEnabled,
                feature.EnabledAt,
                feature.RequiredScopes
            )
        );
    }
}
