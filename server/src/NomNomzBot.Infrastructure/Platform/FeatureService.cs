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
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform;

public class FeatureService : IFeatureService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IEventBus _eventBus;

    public FeatureService(IApplicationDbContext db, TimeProvider timeProvider, IEventBus eventBus)
    {
        _db = db;
        _timeProvider = timeProvider;
        _eventBus = eventBus;
    }

    // The static catalogue of all opt-in channel features.
    // key → (label, description, required Twitch scopes, default state when no ChannelFeature row exists yet)
    private static readonly IReadOnlyDictionary<
        string,
        (string Label, string Description, string[] Scopes, bool DefaultOn)
    > Catalogue = new Dictionary<string, (string, string, string[], bool)>(StringComparer.Ordinal)
    {
        ["custom_code"] = (
            "Custom Code",
            "Author Lua scripts to use as pipeline actions for advanced automation.",
            [],
            false
        ),
        // The four chat-decoration toggles (chat-decoration spec §5/§9·9). Third-party emote rendering is ON by
        // default — the near-universal want, matching every emote extension — and a channel opts OUT with an
        // explicit toggle. Link preview makes an outbound fetch per shared link, so it is opt-in (off by default).
        ["use_7tv"] = (
            "7TV Emotes",
            "Render 7TV third-party emotes — including animated and zero-width overlay emotes — in chat.",
            [],
            true
        ),
        ["use_bttv"] = (
            "BetterTTV Emotes",
            "Render BetterTTV (BTTV) third-party emotes in chat.",
            [],
            true
        ),
        ["use_ffz"] = (
            "FrankerFaceZ Emotes",
            "Render FrankerFaceZ (FFZ) third-party emotes in chat.",
            [],
            true
        ),
        ["use_link_preview"] = (
            "Link Previews",
            "Show an OpenGraph preview (title, description, image) for links shared by subscribers and above. "
                + "Off by default because it makes an outbound fetch for every shared link.",
            [],
            false
        ),
    };

    /// <summary>The key's resting state when no <see cref="ChannelFeature"/> row exists yet — false for an unrecognized key.</summary>
    private static bool DefaultOnFor(string featureKey) =>
        Catalogue.TryGetValue(
            featureKey,
            out (string Label, string Description, string[] Scopes, bool DefaultOn) meta
        ) && meta.DefaultOn;

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

        // Return every catalogue entry; fall back to the KEY'S OWN default (not blanket-disabled) when no row
        // exists yet — a channel that has never touched "use_7tv" still reports it enabled, matching the
        // decorator's own default-on behavior for third-party emotes.
        List<FeatureStatusDto> features =
        [
            .. Catalogue.Select(entry =>
            {
                existing.TryGetValue(entry.Key, out ChannelFeature? row);
                return new FeatureStatusDto(
                    entry.Key,
                    entry.Value.Label,
                    entry.Value.Description,
                    row?.IsEnabled ?? entry.Value.DefaultOn,
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
            // Seed a missing row at the key's own default (true for the emote-provider keys, false otherwise) —
            // a channel with no row yet is sitting at that default, so the flip below moves it away in ONE call
            // instead of always materializing an enabled row that a default-ON key would then need a second
            // click to turn off.
            feature = new()
            {
                BroadcasterId = broadcasterId,
                FeatureKey = featureKey,
                IsEnabled = DefaultOnFor(featureKey),
            };
            _db.ChannelFeatures.Add(feature);
        }

        feature.IsEnabled = !feature.IsEnabled;
        feature.EnabledAt = feature.IsEnabled ? _timeProvider.GetUtcNow().UtcDateTime : null;

        await _db.SaveChangesAsync(cancellationToken);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "features",
                EntityId = feature.FeatureKey,
                Action = "toggled",
            },
            cancellationToken
        );

        Catalogue.TryGetValue(
            feature.FeatureKey,
            out (string Label, string Description, string[] Scopes, bool DefaultOn) meta
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
