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

namespace NomNomzBot.Api.Hubs.Clients;

public interface IOverlayClient
{
    Task WidgetEvent(WidgetEventDto evt);
    Task WidgetReload();
    Task WidgetSettingsChanged(WidgetSettingsDto settings);

    /// <summary>
    /// The generic overlay event feed (widgets-overlays.md): one channel-wide event, delivered to every overlay
    /// connection so a custom overlay can listen and filter client-side. <see cref="OverlayEventDto.Payload"/> is
    /// the event's data as a raw JSON string the overlay parses.
    /// </summary>
    Task Event(OverlayEventDto evt);

    /// <summary>Instructs the overlay's audio bus to start playing a clip (spec §4).</summary>
    Task PlaySound(PlaySoundPayload payload);

    /// <summary>Instructs the overlay to stop a named clip handle, or all playback when <see cref="StopSoundPayload.All"/> is true.</summary>
    Task StopSound(StopSoundPayload payload);
}
