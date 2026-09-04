// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace NomNomzBot.Infrastructure.Music.Realtime;

// Wire shapes for Spotify's undocumented realtime dealer socket (wss://dealer.spotify.com/), reverse
// engineered from the legacy reference implementation (nomercy-bot's SpotifyWebsocketService +
// Spotify/Dto/SpotifyState.cs) — the same shape the Spotify web player itself consumes. Small, tightly
// coupled wire POCOs kept in one file, matching that legacy Dto convention rather than one file each.

internal sealed class SpotifyDealerEnvelope
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonPropertyName("headers")]
    public SpotifyDealerHeaders? Headers { get; init; }

    [JsonPropertyName("payloads")]
    public SpotifyDealerPayload[]? Payloads { get; init; }
}

internal sealed class SpotifyDealerHeaders
{
    [JsonPropertyName("Spotify-Connection-Id")]
    public string? SpotifyConnectionId { get; init; }
}

internal sealed class SpotifyDealerPayload
{
    [JsonPropertyName("events")]
    public SpotifyDealerEventElement[]? Events { get; init; }
}

internal sealed class SpotifyDealerEventElement
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("event")]
    public SpotifyDealerEventBody? Event { get; init; }
}

internal sealed class SpotifyDealerEventBody
{
    [JsonPropertyName("state")]
    public SpotifyDealerPlayerState? State { get; init; }
}

internal sealed class SpotifyDealerPlayerState
{
    [JsonPropertyName("is_playing")]
    public bool IsPlaying { get; init; }

    [JsonPropertyName("progress_ms")]
    public int ProgressMs { get; init; }

    [JsonPropertyName("shuffle_state")]
    public bool ShuffleState { get; init; }

    [JsonPropertyName("repeat_state")]
    public string? RepeatState { get; init; }

    [JsonPropertyName("item")]
    public SpotifyDealerItem? Item { get; init; }

    [JsonPropertyName("device")]
    public SpotifyDealerDevice? Device { get; init; }
}

internal sealed class SpotifyDealerItem
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; init; }

    [JsonPropertyName("artists")]
    public SpotifyDealerArtist[]? Artists { get; init; }

    [JsonPropertyName("album")]
    public SpotifyDealerAlbum? Album { get; init; }
}

internal sealed class SpotifyDealerArtist
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class SpotifyDealerAlbum
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("images")]
    public SpotifyDealerImage[]? Images { get; init; }
}

internal sealed class SpotifyDealerImage
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

internal sealed class SpotifyDealerDevice
{
    [JsonPropertyName("volume_percent")]
    public int? VolumePercent { get; init; }
}
