// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Spotify has blocked the channel's Spotify app for a long time (a <c>Retry-After</c> of hours, the daily
/// quota of an app in development mode), so music commands and now-playing pause until it ends. The item
/// names that end time and points to the fix: a new Spotify app's keys on the Integrations page. A short
/// 429 is not reported — it clears on its own within seconds. The key embeds the end time, so a dismissed
/// block stays hidden and the next one surfaces again.
/// </summary>
public sealed class SpotifyAppBlockedSource(
    ISpotifyRateLimitCooldowns cooldowns,
    TimeProvider clock
) : IActionRequiredSource
{
    private const string KeyPrefix = "spotify-blocked:";

    /// <summary>The shortest cooldown worth telling the streamer about.</summary>
    public static readonly TimeSpan LongBlock = TimeSpan.FromMinutes(10);

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    // The cooldown store pushes the inbox change itself when a cooldown starts or ends.
    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

    public Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !cooldowns.TryGetCooldown(channelId, clock.GetUtcNow(), out SpotifyCooldown cooldown)
            || cooldown.Length < LongBlock
        )
            return Task.FromResult(Result.Success<List<ActionRequiredItemDto>>([]));

        string key = $"{KeyPrefix}{channelId}:{cooldown.Until.UtcTicks}";
        if (dismissedKeys.Contains(key))
            return Task.FromResult(Result.Success<List<ActionRequiredItemDto>>([]));

        ActionRequiredItemDto item = new(
            Id: key,
            Kind: "spotify_app_blocked",
            Severity: "warning",
            TitleKey: "attention_spotify_blocked_title",
            MessageKey: "attention_spotify_blocked_message",
            Parameters: new()
            {
                ["until"] = cooldown
                    .Until.ToUniversalTime()
                    .ToString("O", CultureInfo.InvariantCulture),
            },
            DetectedAt: cooldown.Since.UtcDateTime,
            DeepLinkRoute: "integrations",
            SourceUserId: null,
            SourceUserName: null,
            Count: 1,
            QueueItemIds: []
        );
        return Task.FromResult(Result.Success<List<ActionRequiredItemDto>>([item]));
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));
}
