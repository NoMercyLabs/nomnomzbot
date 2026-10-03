// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Adapts the Application-layer <see cref="ITtsOverlayNotifier"/> abstraction to the <see cref="IWidgetNotifier"/>
/// SignalR hub — bridges the Infrastructure→API dependency boundary so <c>TtsDispatchService</c>'s <c>client_edge</c>
/// leg never takes a direct reference to the SignalR layer.
/// </summary>
internal sealed class TtsOverlayNotifierAdapter : ITtsOverlayNotifier
{
    private readonly IWidgetNotifier _notifier;

    private readonly IChannelAudioMixService _mix;

    public TtsOverlayNotifierAdapter(IWidgetNotifier notifier, IChannelAudioMixService mix)
    {
        _notifier = notifier;
        _mix = mix;
    }

    public async Task SpeakAsync(
        Guid broadcasterId,
        TtsOverlaySpeakDto payload,
        CancellationToken ct = default
    )
    {
        Result<ChannelAudioMixDto> mix = await _mix.GetAsync(broadcasterId, ct);
        TtsSpeakOptions? options = mix.IsSuccess
            ? new TtsSpeakOptions(null, null, mix.Value.TtsPlaybackVolume)
            : null;

        await _notifier.TtsSpeakAsync(
            broadcasterId.ToString(),
            new(
                broadcasterId,
                payload.Text,
                payload.VoiceId,
                payload.Provider,
                payload.CueId,
                options,
                payload.Locale
            ),
            ct
        );
    }
}
