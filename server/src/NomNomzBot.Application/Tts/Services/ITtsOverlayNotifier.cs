// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Tts.Services;

/// <summary>
/// Abstraction that lets <c>TtsDispatchService</c> push a <c>client_edge</c> utterance to the overlay without
/// taking a direct dependency on the API layer's SignalR hub context (tts.md §3.4). The browser-source widget
/// synthesizes and speaks the utterance edge-side — no server audio bytes cross the wire. Implemented by
/// <c>TtsOverlayNotifierAdapter</c> in the API layer; a no-op fallback runs in worker/test contexts.
/// </summary>
public interface ITtsOverlayNotifier
{
    Task SpeakAsync(Guid broadcasterId, TtsOverlaySpeakDto payload, CancellationToken ct = default);
}

/// <summary>The utterance a client-edge overlay renders: the spoken text + the resolved provider voice.</summary>
public sealed record TtsOverlaySpeakDto(
    string Text,
    string VoiceId,
    string Provider,
    string? CueId
);
