// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
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
}
