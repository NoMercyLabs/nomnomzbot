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

    // The static catalogue of all opt-in channel features.
    // key → (label, description, required Twitch scopes)
    private static readonly IReadOnlyDictionary<
        string,
        (string Label, string Description, string[] Scopes)
    > Catalogue = new Dictionary<string, (string, string, string[])>(StringComparer.Ordinal)
    {
        ["custom_code"] = (
            "Custom Code",
            "Author Lua scripts to use as pipeline actions for advanced automation.",
            []
        ),
    };

    public async Task<Result<List<FeatureStatusDto>>> GetFeaturesAsync(
        string channelId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return Result.Success(new List<FeatureStatusDto>());

        // Index existing opt-in rows so we can left-join the catalogue.
        Dictionary<string, ChannelFeature> existing = (
            await _db
                .ChannelFeatures.Where(f => f.BroadcasterId == broadcasterId)
                .ToListAsync(cancellationToken)
        ).ToDictionary(f => f.FeatureKey, StringComparer.Ordinal);

        // Return every catalogue entry; fall back to disabled/no-scopes when no row exists yet.
        List<FeatureStatusDto> features =
        [
            .. Catalogue.Select(entry =>
            {
                existing.TryGetValue(entry.Key, out ChannelFeature? row);
                return new FeatureStatusDto(
                    entry.Key,
                    entry.Value.Label,
                    entry.Value.Description,
                    row?.IsEnabled ?? false,
                    row?.EnabledAt,
                    row?.RequiredScopes ?? entry.Value.Scopes
                );
            }),
        ];

        return Result.Success(features);
    }

    public async Task<bool> IsFeatureEnabledAsync(
        string channelId,
        string featureKey,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return false;

        ChannelFeature? row = await _db.ChannelFeatures.FirstOrDefaultAsync(
            f => f.BroadcasterId == broadcasterId && f.FeatureKey == featureKey,
            cancellationToken
        );
        return row is { IsEnabled: true };
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

        Catalogue.TryGetValue(
            feature.FeatureKey,
            out (string Label, string Description, string[] Scopes) meta
        );
        return Result.Success(
            new FeatureStatusDto(
                feature.FeatureKey,
                meta.Label ?? feature.FeatureKey,
                meta.Description ?? string.Empty,
                feature.IsEnabled,
                feature.EnabledAt,
                feature.RequiredScopes
            )
        );
    }
}
