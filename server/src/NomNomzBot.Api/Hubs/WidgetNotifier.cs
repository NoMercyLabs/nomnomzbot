// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Hubs;

public interface IWidgetNotifier
{
    Task SendWidgetEventAsync(
        string broadcasterId,
        string widgetId,
        WidgetEventDto dto,
        CancellationToken ct = default
    );
    Task ReloadWidgetAsync(string broadcasterId, string widgetId, CancellationToken ct = default);
    Task SendSettingsChangedAsync(
        string broadcasterId,
        string widgetId,
        WidgetSettingsDto dto,
        CancellationToken ct = default
    );

    /// <summary>Pushes a compile-failed notice to a widget's group (a connected editor surfaces the build error).</summary>
    Task SendCompileFailedAsync(
        string broadcasterId,
        string widgetId,
        WidgetCompileFailedDto dto,
        CancellationToken ct = default
    );

    /// <summary>Pushes a play-sound command to all overlay clients for the given broadcaster.</summary>
    Task PlaySoundAsync(
        string broadcasterId,
        PlaySoundPayload payload,
        CancellationToken ct = default
    );

    /// <summary>Pushes a stop-sound command to all overlay clients for the given broadcaster.</summary>
    Task StopSoundAsync(
        string broadcasterId,
        StopSoundPayload payload,
        CancellationToken ct = default
    );

    /// <summary>Pushes a client-edge TTS utterance to all overlay clients for the given broadcaster.</summary>
    Task TtsSpeakAsync(
        string broadcasterId,
        TtsSpeakPayload payload,
        CancellationToken ct = default
    );

    /// <summary>Pushes a TTS queue command to the channel's one audio page.</summary>
    Task TtsQueueControlAsync(string broadcasterId, string action, CancellationToken ct = default);

    /// <summary>Broadcasts one generic overlay-feed event to every overlay client that hosts no widget.</summary>
    Task BroadcastOverlayEventAsync(
        string broadcasterId,
        OverlayEventDto evt,
        CancellationToken ct = default
    );

    /// <summary>Pushes a moderation retraction to every overlay client for the given broadcaster (widgets-overlays.md §2a).</summary>
    Task RetractAsync(string broadcasterId, RetractPayload payload, CancellationToken ct = default);
}

public class WidgetNotifier : IWidgetNotifier
{
    private readonly IHubContext<OverlayHub, IOverlayClient> _hub;
    private readonly IOverlayPresenceRegistry _presence;

    public WidgetNotifier(
        IHubContext<OverlayHub, IOverlayClient> hub,
        IOverlayPresenceRegistry presence
    )
    {
        _hub = hub;
        _presence = presence;
    }

    // Audio plays on exactly one page: the registry names it, nothing is sent when no page is open.
    private IOverlayClient? AudioPage(string broadcasterId)
    {
        string? connectionId = Guid.TryParse(broadcasterId, out Guid parsed)
            ? _presence.GetAudioTarget(parsed)
            : null;
        return connectionId is null ? null : _hub.Clients.Client(connectionId);
    }

    public Task SendWidgetEventAsync(
        string broadcasterId,
        string widgetId,
        WidgetEventDto dto,
        CancellationToken ct = default
    ) => _hub.Clients.Group($"widget-{broadcasterId}-{widgetId}").WidgetEvent(dto);

    public Task ReloadWidgetAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken ct = default
    ) => _hub.Clients.Group($"widget-{broadcasterId}-{widgetId}").WidgetReload();

    public Task SendSettingsChangedAsync(
        string broadcasterId,
        string widgetId,
        WidgetSettingsDto dto,
        CancellationToken ct = default
    ) => _hub.Clients.Group($"widget-{broadcasterId}-{widgetId}").WidgetSettingsChanged(dto);

    public Task SendCompileFailedAsync(
        string broadcasterId,
        string widgetId,
        WidgetCompileFailedDto dto,
        CancellationToken ct = default
    ) => _hub.Clients.Group($"widget-{broadcasterId}-{widgetId}").WidgetCompileFailed(dto);

    public Task PlaySoundAsync(
        string broadcasterId,
        PlaySoundPayload payload,
        CancellationToken ct = default
    ) => AudioPage(broadcasterId)?.PlaySound(payload) ?? Task.CompletedTask;

    public Task StopSoundAsync(
        string broadcasterId,
        StopSoundPayload payload,
        CancellationToken ct = default
    ) => AudioPage(broadcasterId)?.StopSound(payload) ?? Task.CompletedTask;

    public Task TtsSpeakAsync(
        string broadcasterId,
        TtsSpeakPayload payload,
        CancellationToken ct = default
    ) => AudioPage(broadcasterId)?.TtsSpeak(payload) ?? Task.CompletedTask;

    public Task TtsQueueControlAsync(
        string broadcasterId,
        string action,
        CancellationToken ct = default
    ) => AudioPage(broadcasterId)?.TtsQueueControl(new(action)) ?? Task.CompletedTask;

    public Task BroadcastOverlayEventAsync(
        string broadcasterId,
        OverlayEventDto evt,
        CancellationToken ct = default
    ) => _hub.Clients.Group($"overlay-feed-{broadcasterId}").Event(evt);

    public Task RetractAsync(
        string broadcasterId,
        RetractPayload payload,
        CancellationToken ct = default
    ) => _hub.Clients.Group($"overlay-{broadcasterId}").Retract(payload);
}
