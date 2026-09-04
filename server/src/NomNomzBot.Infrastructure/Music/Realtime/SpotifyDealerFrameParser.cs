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
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music.Realtime;

/// <summary>
/// Recognises the two dealer frame shapes <see cref="SpotifyDealerConnection"/> acts on: the connection-id
/// handshake (needed to PUT the player-notification subscription) and a <c>PLAYER_STATE_CHANGED</c> cluster
/// event. Every other frame shape (pings, other cluster event types) is silently ignored — undocumented wire
/// format, so anything unrecognised is a no-op, never a thrown error.
/// </summary>
internal static class SpotifyDealerFrameParser
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    internal static bool TryGetConnectionId(string rawFrame, out string? connectionId)
    {
        connectionId = TryDeserialize(rawFrame)?.Headers?.SpotifyConnectionId;
        return !string.IsNullOrEmpty(connectionId);
    }

    internal static bool TryParsePlayerStateChanged(
        string rawFrame,
        out SpotifyPlayerStateChangedFrame? frame
    )
    {
        frame = null;

        SpotifyDealerPlayerState? state = TryDeserialize(rawFrame)
            ?.Payloads?.Where(p => p.Events is not null)
            .SelectMany(p => p.Events!)
            .FirstOrDefault(e => e is { Type: "PLAYER_STATE_CHANGED", Event.State: not null })
            ?.Event?.State;

        if (state is null)
            return false;

        SpotifyDealerItem? item = state.Item;
        frame = new SpotifyPlayerStateChangedFrame(
            IsPlaying: state.IsPlaying,
            ProgressMs: state.ProgressMs,
            TrackName: item?.Name,
            Artist: item?.Artists is { Length: > 0 } artists
                ? string.Join(", ", artists.Select(a => a.Name))
                : null,
            ArtistId: item?.Artists?.FirstOrDefault()?.Id,
            Album: item?.Album?.Name,
            AlbumArtUrl: item?.Album?.Images?.FirstOrDefault()?.Url,
            DurationMs: item?.DurationMs ?? 0,
            TrackUri: item?.Uri,
            ShuffleEnabled: state.ShuffleState,
            RepeatMode: MapRepeatMode(state.RepeatState),
            VolumePercent: state.Device?.VolumePercent ?? 100
        );
        return true;
    }

    private static MusicRepeatMode MapRepeatMode(string? repeatState) =>
        repeatState switch
        {
            "track" => MusicRepeatMode.Track,
            "context" => MusicRepeatMode.Context,
            _ => MusicRepeatMode.Off,
        };

    private static SpotifyDealerEnvelope? TryDeserialize(string rawFrame)
    {
        try
        {
            return JsonSerializer.Deserialize<SpotifyDealerEnvelope>(rawFrame, WireJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The track-identity + play-state fields a <c>PLAYER_STATE_CHANGED</c> dealer frame carries,
/// shaped for direct mapping onto <see cref="NomNomzBot.Domain.Music.Events.PlaybackStateChangedEvent"/>.
/// Deliberately excludes the per-action Can* permission flags — the dealer's restriction shape is not the
/// same as the documented Web API's <c>actions.disallows</c>, so <see cref="SpotifyDealerConnection"/> leaves
/// them at the event's own all-permitted defaults rather than guessing at an unverified field.</summary>
internal sealed record SpotifyPlayerStateChangedFrame(
    bool IsPlaying,
    int ProgressMs,
    string? TrackName,
    string? Artist,
    string? ArtistId,
    string? Album,
    string? AlbumArtUrl,
    int DurationMs,
    string? TrackUri,
    bool ShuffleEnabled,
    MusicRepeatMode RepeatMode,
    int VolumePercent
);
