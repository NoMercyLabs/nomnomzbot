// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// What the Music Player migration cannot do for the streamer (music-playback-determinism.md): add the Music
/// Player page to OBS, and look at custom widgets that play audio or read Spotify directly. The migration never
/// edits custom widgets, so it reports them. Every item is derived on read, so it clears the moment its
/// condition clears. The two widget lists carry a hash of their members in the id, so a dismissed list comes
/// back when a new widget joins it.
/// </summary>
public sealed class MusicPlayerMigrationSource(
    IApplicationDbContext db,
    IOverlayPresenceRegistry presence,
    TimeProvider clock
) : IActionRequiredSource
{
    private const string MissingKey = "music-player-missing";
    private const string AudioPrefix = "music-audio-widgets:";
    private const string SpotifyPrefix = "music-spotify-widgets:";
    private const string MusicPlayerName = "Music Player";
    private const string SpotifyApiHost = "api.spotify.com";
    private const string CustomSource = "custom";

    public string SourceKey => "music_player_migration";

    public IReadOnlyCollection<string> KeyPrefixes { get; } =
    [MissingKey, AudioPrefix, SpotifyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(WidgetBuildSucceededEvent), nameof(ChannelConfigChangedEvent)];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        List<Widget> widgets = await db
            .Widgets.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(w => w.BroadcasterId == channelId && w.DeletedAt == null)
            .ToListAsync(cancellationToken);

        List<ActionRequiredItemDto> items = [];
        if (
            widgets.Any(w => w.Source != CustomSource && w.Name == MusicPlayerName)
            && !presence.IsMusicSourceConnected(channelId)
            && !dismissedKeys.Contains(MissingKey)
        )
            items.Add(ToMissingItem());

        List<Widget> custom = [.. widgets.Where(w => w.Source == CustomSource)];
        AddList(
            items,
            dismissedKeys,
            AudioPrefix,
            "custom_widget_audio",
            "attention_custom_widget_audio_title",
            "attention_custom_widget_audio_message",
            [.. custom.Where(HasAudioOn)]
        );
        AddList(
            items,
            dismissedKeys,
            SpotifyPrefix,
            "custom_widget_spotify",
            "attention_custom_widget_spotify_title",
            "attention_custom_widget_spotify_message",
            await ReadingSpotifyAsync(channelId, custom, cancellationToken)
        );

        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static bool HasAudioOn(Widget widget) =>
        widget.Settings.TryGetValue("enableAudio", out object? value)
        && (
            value is true
            || (value is JsonElement { ValueKind: JsonValueKind.True })
            || (value is string text && bool.TryParse(text, out bool parsed) && parsed)
        );

    private async Task<List<Widget>> ReadingSpotifyAsync(
        Guid channelId,
        List<Widget> custom,
        CancellationToken cancellationToken
    )
    {
        List<Guid> versionIds =
        [
            .. custom
                .Where(w => w.ActiveVersionId is not null)
                .Select(w => w.ActiveVersionId!.Value),
        ];
        if (versionIds.Count == 0)
            return [];

        List<WidgetVersion> versions = await db
            .WidgetVersions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.BroadcasterId == channelId && versionIds.Contains(v.Id))
            .ToListAsync(cancellationToken);
        HashSet<Guid> reading =
        [
            .. versions
                .Where(v =>
                    (
                        v.SourceCode?.Contains(SpotifyApiHost, StringComparison.OrdinalIgnoreCase)
                        ?? false
                    )
                    || (
                        v.FilesJson?.Contains(SpotifyApiHost, StringComparison.OrdinalIgnoreCase)
                        ?? false
                    )
                )
                .Select(v => v.Id),
        ];

        return [.. custom.Where(w => w.ActiveVersionId is { } id && reading.Contains(id))];
    }

    private void AddList(
        List<ActionRequiredItemDto> items,
        IReadOnlySet<string> dismissedKeys,
        string prefix,
        string kind,
        string titleKey,
        string messageKey,
        List<Widget> widgets
    )
    {
        if (widgets.Count == 0)
            return;

        List<Widget> ordered =
        [
            .. widgets.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Id),
        ];
        string id = prefix + MembersHash(ordered);
        if (dismissedKeys.Contains(id))
            return;

        items.Add(
            new(
                Id: id,
                Kind: kind,
                Severity: "info",
                TitleKey: titleKey,
                MessageKey: messageKey,
                Parameters: new()
                {
                    ["widgetNames"] = string.Join(", ", ordered.Select(w => w.Name)),
                },
                DetectedAt: clock.GetUtcNow().UtcDateTime,
                DeepLinkRoute: "overlays",
                SourceUserId: null,
                SourceUserName: null,
                Count: ordered.Count,
                QueueItemIds: []
            )
        );
    }

    private static string MembersHash(List<Widget> ordered)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('|', ordered.Select(w => w.Id.ToString("N"))))
        );
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    private ActionRequiredItemDto ToMissingItem() =>
        new(
            Id: MissingKey,
            Kind: "music_player_missing",
            Severity: "warning",
            TitleKey: "attention_music_player_missing_title",
            MessageKey: "attention_music_player_missing_message",
            Parameters: new(),
            DetectedAt: clock.GetUtcNow().UtcDateTime,
            DeepLinkRoute: "overlays",
            SourceUserId: null,
            SourceUserName: null,
            Count: 1,
            QueueItemIds: []
        );
}
