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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Pushes a recorded TTS utterance to dashboard clients as <c>TtsUtteranceSpoken</c>. The payload has the same
/// shape as one row of the spoken history and never the audio, so the history list prepends it with no refetch.
/// </summary>
public sealed class TtsSpokenBroadcastHandler : IEventHandler<TtsUtteranceDispatchedEvent>
{
    /// <summary>The dashboard hub event name.</summary>
    public const string HubEventName = "TtsUtteranceSpoken";

    private readonly IDashboardNotifier _notifier;

    public TtsSpokenBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(TtsUtteranceDispatchedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty || @event.UsageRecordId is null)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            HubEventName,
            new TtsUtteranceSpokenAlertDto(
                @event.UsageRecordId.Value,
                @event.Text,
                @event.VoiceId,
                @event.Provider,
                @event.RequestedByTwitchUserId,
                @event.OccurredAt.UtcDateTime
            ),
            ct
        );
    }
}
