// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.AutomationApi.Events;

/// <summary>
/// The public <c>song.changed</c> automation event (music-automation-controls.md D4) — wraps
/// <see cref="SongChangedEvent"/>, the position-anchor now-playing shape
/// <see cref="Infrastructure.Music.SongChangedProjector"/> resolves via
/// <see cref="MusicAutomationProjection"/>. A field-for-field passthrough: the event was already built
/// from the shared projection at publish time, so this descriptor never re-derives state — the source
/// event IS the wire payload, guaranteeing it never drifts from <c>GetNowPlayingAsync</c>'s live read
/// for the same playback state.
/// </summary>
public sealed class SongChangedAutomationEventDescriptor : IAutomationEventDescriptor
{
    public string PublicName => "song.changed";
    public string Description => "The broadcaster's now-playing track/state changed.";
    public Type DomainEventType => typeof(SongChangedEvent);

    public object ProjectPayload(DomainEventBase domainEvent)
    {
        SongChangedEvent e = (SongChangedEvent)domainEvent;
        return new AutomationNowPlayingDto(
            e.Title,
            e.Artist,
            e.DurationMs,
            e.PositionMs,
            e.IsPlaying,
            e.ShuffleEnabled,
            e.RepeatMode,
            e.IsSaved,
            e.OccurredAt,
            e.VolumePercent,
            e.AlbumArtUrl
        );
    }
}
