// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using FluentAssertions;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;
using Xunit;

namespace NomNomzBot.Api.Tests.Requirements.Providers;

/// <summary>
/// REQUIREMENT: the Spotify integration must expose every Spotify Web API capability the bot's music use-cases
/// need — playback transport, queue, search, devices, playlists, and library. The bot models providers through
/// a capability-gated seam (<see cref="IMusicProvider"/> + <see cref="IMusicRemoteProvider"/> +
/// <see cref="IMusicProviderManageApi"/>); <see cref="SpotifyMusicProvider"/> is the concrete Spotify adapter.
/// These tests enumerate the method surface that EXISTS and the capability flags Spotify DECLARES, and demand
/// they cover what the Spotify Web API allows. A red is a Spotify capability the bot does not yet expose.
/// </summary>
public sealed class SpotifyCoverageTests
{
    private static HashSet<string> MusicManageSurface() =>
        ProviderSurface.MethodNames(
            typeof(IMusicProvider),
            typeof(IMusicRemoteProvider),
            typeof(IMusicProviderManageApi)
        );

    [Fact]
    public void Music_seam_exposes_a_method_for_every_spotify_web_api_capability()
    {
        HashSet<string> methods = MusicManageSurface();

        (string Capability, string[] Keywords)[] expected =
        [
            // ── Playback / transport (Spotify Player API) ──
            ("Play", ["Play"]),
            ("Pause", ["Pause"]),
            ("Skip to next", ["Skip"]),
            ("Skip to previous", ["Previous"]),
            ("Seek to position", ["Seek"]),
            ("Set playback volume", ["SetVolume"]),
            ("Toggle shuffle", ["SetShuffle"]),
            ("Set repeat mode", ["SetRepeat"]),
            ("Get currently playing / now playing", ["GetCurrentTrack"]),
            ("Add item to queue", ["AddToQueue"]),
            // ── Devices ──
            ("Get available devices", ["GetDevices"]),
            ("Transfer playback", ["TransferPlayback"]),
            // ── Search / resolve ──
            ("Search for item", ["Search"]),
            ("Get track (resolve)", ["ResolveTrack"]),
            // ── Playlists ──
            ("Get user playlists", ["ListPlaylists", "GetPlaylists"]),
            ("Create playlist", ["CreatePlaylist"]),
            ("Change playlist details", ["UpdatePlaylist"]),
            ("Unfollow (delete) playlist", ["DeletePlaylist"]),
            ("Add items to playlist", ["AddPlaylistTracks"]),
            ("Remove items from playlist", ["RemovePlaylistTracks"]),
            ("Start context playback", ["PlayContext"]),
            // ── Library / saved tracks ──
            ("Save tracks for user", ["SaveTracks"]),
            ("Remove saved tracks", ["RemoveSavedTracks"]),
            ("Get user saved tracks", ["GetSavedTracks"]),
            ("Check saved tracks", ["AreTracksSaved"]),
            // ── Follow ──
            ("Follow artist/playlist", ["Follow"]),
            ("Unfollow artist/playlist", ["Unfollow"]),
        ];

        List<string> missing = expected
            .Where(capability => !methods.Covers(capability.Keywords))
            .Select(capability => capability.Capability)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "the bot must expose every Spotify Web API capability its music use-cases rely on"
            );
    }

    [Fact]
    public void Spotify_adapter_declares_every_capability_its_web_api_supports()
    {
        // The Capabilities getter is a pure expression over enum constants, so it can be read from an
        // uninitialized instance without constructing the provider's HTTP/token/DB dependencies.
        IMusicProvider spotify = (IMusicProvider)
            RuntimeHelpers.GetUninitializedObject(typeof(SpotifyMusicProvider));

        MusicProviderCapabilities declared = spotify.Capabilities;

        MusicProviderCapabilities[] expected =
        [
            MusicProviderCapabilities.Search,
            MusicProviderCapabilities.Queue,
            MusicProviderCapabilities.PlaybackControl,
            MusicProviderCapabilities.Volume,
            MusicProviderCapabilities.Skip,
            MusicProviderCapabilities.Seek,
            MusicProviderCapabilities.NowPlaying,
            MusicProviderCapabilities.Previous,
            MusicProviderCapabilities.Shuffle,
            MusicProviderCapabilities.Repeat,
            MusicProviderCapabilities.TransferDevice,
            MusicProviderCapabilities.Library,
            MusicProviderCapabilities.Playlists,
        ];

        List<MusicProviderCapabilities> missing = expected
            .Where(capability => !declared.HasFlag(capability))
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "the Spotify adapter must advertise every capability the Spotify Web API supports so routing "
                    + $"exposes it; it declares [{declared}]"
            );
    }

    [Fact]
    public void Spotify_adapter_implements_the_full_provider_seam()
    {
        typeof(IMusicProvider)
            .IsAssignableFrom(typeof(SpotifyMusicProvider))
            .Should()
            .BeTrue("Spotify must implement the unified playback/queue seam");

        typeof(IMusicRemoteProvider)
            .IsAssignableFrom(typeof(SpotifyMusicProvider))
            .Should()
            .BeTrue("Spotify must implement the paged-playlists / play-context seam");

        typeof(IMusicProviderManageApi)
            .IsAssignableFrom(typeof(SpotifyMusicProvider))
            .Should()
            .BeTrue(
                "Spotify must implement the per-user manage surface (playlists/library/follow)"
            );
    }
}
