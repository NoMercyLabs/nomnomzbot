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

    public FeatureService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Result<List<FeatureStatusDto>>> GetFeaturesAsync(
        string channelId,
        CancellationToken cancellationToken = default
    )
    {
        List<FeatureStatusDto> features = await _db
            .ChannelFeatures.Where(f => f.BroadcasterId == channelId)
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
        ChannelFeature? feature = await _db.ChannelFeatures.FirstOrDefaultAsync(
            f => f.BroadcasterId == channelId && f.FeatureKey == featureKey,
            cancellationToken
        );

        if (feature is null)
        {
            feature = new()
            {
                BroadcasterId = channelId,
                FeatureKey = featureKey,
                IsEnabled = true,
                EnabledAt = DateTime.UtcNow,
            };
            _db.ChannelFeatures.Add(feature);
        }
        else
        {
            feature.IsEnabled = !feature.IsEnabled;
            feature.EnabledAt = feature.IsEnabled ? DateTime.UtcNow : null;
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
