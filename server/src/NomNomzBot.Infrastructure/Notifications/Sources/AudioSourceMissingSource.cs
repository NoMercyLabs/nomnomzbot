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
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// No Audio Source page is open. While the channel is live this is critical, with or without other overlay
/// pages: TTS and sound have nowhere to play. While offline it is a warning, and only when other overlay pages
/// are open, because then sound and TTS play from whichever page opened last. The item clears the moment an
/// Audio Source page connects; the overlay hub signals the inbox on every page join and leave, and the stream
/// online and offline handlers signal it once the live state is saved. The live item is keyed to the open
/// stream, so dismissing it hides only that stream's item and never the offline warning.
/// </summary>
public sealed class AudioSourceMissingSource(
    IApplicationDbContext db,
    IOverlayPresenceRegistry presence,
    TimeProvider clock
) : IActionRequiredSource
{
    private const string Key = "audio-source-missing";
    private const string LiveKeyPrefix = Key + ":live:";

    public string SourceKey => "audio_sources";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [Key];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        if (presence.IsAudioSourceConnected(channelId))
            return Result.Success<List<ActionRequiredItemDto>>([]);

        bool live = await db
            .Channels.AsNoTracking()
            .AnyAsync(c => c.Id == channelId && c.IsLive, cancellationToken);
        if (live)
        {
            string? streamId = await db
                .Streams.AsNoTracking()
                .Where(s => s.ChannelId == channelId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);
            string liveId = LiveKeyPrefix + (streamId ?? "none");
            return Result.Success<List<ActionRequiredItemDto>>(
                dismissedKeys.Contains(liveId) ? [] : [ToItem(liveId, live: true)]
            );
        }

        return Result.Success<List<ActionRequiredItemDto>>(
            presence.IsOverlayConnected(channelId) && !dismissedKeys.Contains(Key)
                ? [ToItem(Key, live: false)]
                : []
        );
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private ActionRequiredItemDto ToItem(string id, bool live) =>
        new(
            Id: id,
            Kind: "audio_source_missing",
            Severity: live ? "critical" : "warning",
            TitleKey: live
                ? "attention_audio_source_missing_live_title"
                : "attention_audio_source_missing_title",
            MessageKey: live
                ? "attention_audio_source_missing_live_message"
                : "attention_audio_source_missing_message",
            Parameters: new(),
            DetectedAt: clock.GetUtcNow().UtcDateTime,
            DeepLinkRoute: live ? "tts" : "widgets",
            SourceUserId: null,
            SourceUserName: null,
            Count: 1,
            QueueItemIds: []
        );
}
