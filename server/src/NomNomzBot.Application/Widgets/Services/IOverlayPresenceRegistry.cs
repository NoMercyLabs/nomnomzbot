// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// Which of a channel's widgets have a browser source actually attached right now.
/// <para>
/// Dispatching a widget event succeeds whether or not anything is listening, so a feature whose only output
/// is a browser source fails completely SILENTLY when the streamer has not added that source: TTS reported
/// every utterance as spoken while the stream heard nothing at all. Anything that speaks through an overlay
/// asks here first, so "nobody is listening" is stated instead of looking like success.
/// </para>
/// </summary>
public interface IOverlayPresenceRegistry
{
    /// <summary>True when at least one live overlay connection has joined <paramref name="widgetId"/>.</summary>
    bool IsWidgetAttached(Guid broadcasterId, Guid widgetId);

    /// <summary>
    /// True when at least one browser source is currently connected to the channel's overlay at all — every
    /// overlay connection joins this broadcaster-wide group on connect, regardless of which widget(s) it
    /// later attaches to. Used by anything that pushes to the shared overlay bus (e.g. sound-clip stop) and
    /// has no single widget to check.
    /// </summary>
    bool IsOverlayConnected(Guid broadcasterId);

    /// <summary>
    /// The one connection every sound clip, redemption sound and TTS line plays on: the most recently joined
    /// Audio source page. Audio never plays on any other widget's page: null when no Audio source page is
    /// open, so the sound stays silent and the missing-Audio-source inbox warning shows.
    /// </summary>
    string? GetAudioTarget(Guid broadcasterId);

    /// <summary>True when an Audio source page (gallery key <c>audio</c>, or its old key <c>tts_audio</c>) is open for the channel.</summary>
    bool IsAudioSourceConnected(Guid broadcasterId);
}
