// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Overlay pages are open but none is an Audio Source page, so every sound and TTS line plays from whichever
/// other page opened last, without the streamer knowing. The item clears the moment an Audio Source page
/// connects; the overlay hub signals the inbox on every page join and leave, so there is no event type here.
/// </summary>
public sealed class AudioSourceMissingSource(IOverlayPresenceRegistry presence, TimeProvider clock)
    : IActionRequiredSource
{
    private const string Key = "audio-source-missing";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [Key];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

    public Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        bool missing =
            presence.IsOverlayConnected(channelId) && !presence.IsAudioSourceConnected(channelId);
        List<ActionRequiredItemDto> items =
            missing && !dismissedKeys.Contains(Key) ? [ToItem()] : [];
        return Task.FromResult(Result.Success(items));
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private ActionRequiredItemDto ToItem() =>
        new(
            Id: Key,
            Kind: "audio_source_missing",
            Severity: "warning",
            TitleKey: "attention_audio_source_missing_title",
            MessageKey: "attention_audio_source_missing_message",
            Parameters: new(),
            DetectedAt: clock.GetUtcNow().UtcDateTime,
            DeepLinkRoute: "widgets",
            SourceUserId: null,
            SourceUserName: null,
            Count: 1,
            QueueItemIds: []
        );
}
