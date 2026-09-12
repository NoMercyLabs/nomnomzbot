// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Extensions.Logging;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Obs.EventHandlers;

/// <summary>
/// Bridges the generic <see cref="ObsEventReceivedEvent"/> (every raw OBS-WS event, obs-control.md
/// §2/§6 — already consumed by <see cref="ObsEventTriggerSource"/> for the <c>obs_event</c> pipeline
/// trigger surface) into the typed live-state domain events
/// (<see cref="ObsStreamingStateChangedEvent"/>/<see cref="ObsRecordingStateChangedEvent"/>/
/// <see cref="ObsInputMuteStateChangedEvent"/>) that feed the public
/// <c>obs.streaming.changed</c>/<c>obs.recording.changed</c>/<c>obs.mute.changed</c> automation events
/// (S-STREAMDECK-OBS-REMAINDER). Mirrors <see cref="Music.SongChangedProjector"/>'s role for
/// <c>song.changed</c>: a thin projector that turns an already-flowing internal signal into the
/// public wire shape, never a new OBS-side subscription.
///
/// Only the three OBS-WS event types the Stream Deck plugin's placeholder icons need
/// (<c>StreamStateChanged</c>, <c>RecordStateChanged</c>, <c>InputMuteStateChanged</c>) are
/// translated; every other <c>ObsEventReceivedEvent</c> is ignored here (it still reaches
/// <see cref="ObsEventTriggerSource"/> unaffected — both handlers run off the same bus event).
/// </summary>
public sealed class ObsAutomationStateForwarder : IEventHandler<ObsEventReceivedEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<ObsAutomationStateForwarder> _logger;

    public ObsAutomationStateForwarder(
        IEventBus eventBus,
        ILogger<ObsAutomationStateForwarder> logger
    )
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task HandleAsync(
        ObsEventReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.ObsEventType))
            return;

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(@event.DataJson) ? "{}" : @event.DataJson
            );
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(
                ex,
                "obs.{EventType} carried unparsable data for {Channel} — live-state forward skipped.",
                @event.ObsEventType,
                @event.BroadcasterId
            );
            return;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            switch (@event.ObsEventType)
            {
                case "StreamStateChanged":
                    await _eventBus.PublishAsync(
                        new ObsStreamingStateChangedEvent
                        {
                            BroadcasterId = @event.BroadcasterId,
                            Active = GetBool(root, "outputActive"),
                        },
                        cancellationToken
                    );
                    break;

                case "RecordStateChanged":
                    string outputState = GetString(root, "outputState");
                    await _eventBus.PublishAsync(
                        new ObsRecordingStateChangedEvent
                        {
                            BroadcasterId = @event.BroadcasterId,
                            Active = GetBool(root, "outputActive"),
                            Paused = outputState.Contains(
                                "PAUSED",
                                StringComparison.OrdinalIgnoreCase
                            ),
                        },
                        cancellationToken
                    );
                    break;

                case "InputMuteStateChanged":
                    string inputName = GetString(root, "inputName");
                    if (string.IsNullOrEmpty(inputName))
                        return;
                    await _eventBus.PublishAsync(
                        new ObsInputMuteStateChangedEvent
                        {
                            BroadcasterId = @event.BroadcasterId,
                            InputName = inputName,
                            Muted = GetBool(root, "inputMuted"),
                        },
                        cancellationToken
                    );
                    break;
            }
        }
    }

    private static bool GetBool(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    private static string GetString(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
