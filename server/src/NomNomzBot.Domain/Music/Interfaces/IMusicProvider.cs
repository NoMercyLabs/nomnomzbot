// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// What a music provider can do (music-sr.md §3.5). Routing and per-operation gates check these
/// flags — never provider names. A member whose required flag is absent fails with the
/// <c>CAPABILITY_UNSUPPORTED</c> result code at the consumer, and the provider member is never called.
/// </summary>
[Flags]
public enum MusicProviderCapabilities
{
    None = 0,

    /// <summary>Can resolve a query → tracks.</summary>
    Search = 1 << 0,

    /// <summary>Has a provider-side queue to push to.</summary>
    Queue = 1 << 1,

    /// <summary>Play/Pause transport control.</summary>
    PlaybackControl = 1 << 2,

    /// <summary>SetVolume on the provider's player.</summary>
    Volume = 1 << 3,

    /// <summary>Skip the current track.</summary>
    Skip = 1 << 4,

    /// <summary>Seek within the current track.</summary>
    Seek = 1 << 5,

    /// <summary>Can report the currently-playing track.</summary>
    NowPlaying = 1 << 6,

    /// <summary>May be routed viewer song requests by the SR pipeline.</summary>
    AcceptsSongRequests = 1 << 7,

    /// <summary>Previous-track on the provider's player.</summary>
    Previous = 1 << 8,

    /// <summary>Toggle shuffle on the provider's player.</summary>
    Shuffle = 1 << 9,

    /// <summary>Set repeat mode (off|track|context).</summary>
    Repeat = 1 << 10,

    /// <summary>Move active playback to another of the user's devices.</summary>
    TransferDevice = 1 << 11,

    /// <summary>Save/remove saved tracks, follow/unfollow, ratings.</summary>
    Library = 1 << 12,

    /// <summary>Create/read/update playlists + add/remove tracks.</summary>
    Playlists = 1 << 13,

    /// <summary>Follow/unfollow channels (YouTube subscriptions).</summary>
    Subscriptions = 1 << 14,
}

/// <summary>Provider repeat mode. <c>Context</c> = playlist/album (Spotify "context").</summary>
public enum MusicRepeatMode
{
    Off,
    Track,
    Context,
}

/// <summary>One of the user's playback devices on the provider (music-sr.md §3.5).</summary>
public sealed record MusicDeviceInfo(
    string Id,
    string Name,
    string Type,
    bool IsActive,
    int? VolumePercent
);

/// <summary>
/// The unified playback/queue seam for music providers (Spotify, YouTube, …) — music-sr.md §3.5.
/// <c>Guid broadcasterId</c> is the tenant key on every member. Capability-gated: consumers check
/// <see cref="Capabilities"/> before calling a gated member; providers never gate internally.
/// </summary>
public interface IMusicProvider
{
    /// <summary>Registry key ("spotify" | "youtube" | …) — selection key for provider priority.</summary>
    string Provider { get; }

    /// <summary>What this provider supports; gates routing instead of name checks.</summary>
    MusicProviderCapabilities Capabilities { get; }

    Task PlayAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    Task PauseAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    Task SkipAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>Provider previous-track. Requires <see cref="MusicProviderCapabilities.Previous"/>.</summary>
    Task PreviousAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>Seeks within the current track. Requires <see cref="MusicProviderCapabilities.Seek"/>.</summary>
    Task SeekAsync(
        Guid broadcasterId,
        int positionSeconds,
        CancellationToken cancellationToken = default
    );

    /// <summary>Toggles provider shuffle. Requires <see cref="MusicProviderCapabilities.Shuffle"/>.</summary>
    Task SetShuffleAsync(
        Guid broadcasterId,
        bool enabled,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sets provider repeat mode. Requires <see cref="MusicProviderCapabilities.Repeat"/>.</summary>
    Task SetRepeatAsync(
        Guid broadcasterId,
        MusicRepeatMode mode,
        CancellationToken cancellationToken = default
    );

    /// <summary>Lists the user's playback devices. Requires <see cref="MusicProviderCapabilities.TransferDevice"/>.</summary>
    Task<IReadOnlyList<MusicDeviceInfo>> GetDevicesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Moves active playback to another device. Requires <see cref="MusicProviderCapabilities.TransferDevice"/>.</summary>
    Task TransferPlaybackAsync(
        Guid broadcasterId,
        string deviceId,
        bool play,
        CancellationToken cancellationToken = default
    );

    Task<TrackInfo?> GetCurrentTrackAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<TrackInfo>> SearchAsync(
        Guid broadcasterId,
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Authoritative single-track metadata lookup (provider track id/uri → <see cref="TrackInfo"/>).
    /// Null if not found/unavailable.
    /// </summary>
    Task<TrackInfo?> ResolveTrackAsync(
        Guid broadcasterId,
        string uriOrId,
        CancellationToken cancellationToken = default
    );

    Task<bool> AddToQueueAsync(
        Guid broadcasterId,
        string trackUri,
        CancellationToken cancellationToken = default
    );
}

public class TrackInfo
{
    public required string TrackName { get; init; }
    public required string Artist { get; init; }
    public required string Album { get; init; }
    public required string TrackUri { get; init; }
    public string? AlbumArtUrl { get; init; }
    public int DurationMs { get; init; }
    public required string Provider { get; init; }

    /// <summary>The provider's own track id (Spotify track id, YouTube video id). Empty when unknown.</summary>
    public string ProviderTrackId { get; init; } = string.Empty;

    /// <summary>Provider-flagged explicit content — enforces the <c>BlockExplicit</c> gate (music-sr.md §3.5).</summary>
    public bool IsExplicit { get; init; }

    /// <summary>Provider-flagged age restriction (YouTube) — enforces the <c>BlockAgeRestricted</c> gate.</summary>
    public bool IsAgeRestricted { get; init; }

    /// <summary>Whether the track can play in the embedded player (YouTube) — enforces the <c>EmbeddableOnly</c> gate.</summary>
    public bool IsEmbeddable { get; init; }

    // Only meaningful on a GetCurrentTrackAsync result (the provider's "currently playing" read) — a
    // SearchAsync hit is not "playing" anything, so it is left at the default (false / 0) there.
    public bool IsPlaying { get; init; }
    public int ProgressMs { get; init; }
}
