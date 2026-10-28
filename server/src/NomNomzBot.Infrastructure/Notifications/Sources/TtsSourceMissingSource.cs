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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Overlay pages are open but no TTS Caption page is. TTS plays only on the TTS Caption page, so the channel
/// hears no TTS at all. The item clears the moment a TTS Caption page connects; the overlay hub signals the
/// inbox on every page join and leave.
/// </summary>
public sealed class TtsSourceMissingSource(IOverlayPresenceRegistry presence, TimeProvider clock)
    : IActionRequiredSource
{
    private const string Key = "tts-source-missing";

    public string SourceKey => "tts_sources";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [Key];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

    public Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        bool missing =
            presence.IsOverlayConnected(channelId)
            && !presence.IsTtsSourceConnected(channelId)
            && !dismissedKeys.Contains(Key);
        return Task.FromResult(
            Result.Success<List<ActionRequiredItemDto>>(missing ? [ToItem()] : [])
        );
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private ActionRequiredItemDto ToItem() =>
        new(
            Id: Key,
            Kind: "tts_source_missing",
            Severity: "warning",
            TitleKey: "attention_tts_source_missing_title",
            MessageKey: "attention_tts_source_missing_message",
            Parameters: new(),
            DetectedAt: clock.GetUtcNow().UtcDateTime,
            DeepLinkRoute: "overlays",
            SourceUserId: null,
            SourceUserName: null,
            Count: 1,
            QueueItemIds: []
        );
}
