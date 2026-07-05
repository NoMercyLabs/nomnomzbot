// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves §3.5's <c>ResolveTrackAsync</c> on the Spotify provider: a share URL / spotify URI / bare
/// id all resolve through GET /tracks/{id} and map the payload field-by-field into the normalized
/// <see cref="TrackInfo"/> (including the new explicit/embeddable gate flags), while an unknown
/// track, an unconnected broadcaster, or an unparseable input fail closed to null — never a throw,
/// never a stray API call.
/// </summary>
public sealed class SpotifyMusicProviderResolveTrackTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f3001");
    private const string TrackId = "4uLU6hMCjMI75M1A2tKUQC";

    private const string TrackJson = """
        {"id":"4uLU6hMCjMI75M1A2tKUQC","name":"Never Gonna Give You Up",
         "uri":"spotify:track:4uLU6hMCjMI75M1A2tKUQC","duration_ms":213573,"explicit":true,
         "artists":[{"name":"Rick Astley"},{"name":"Featured Guest"}],
         "album":{"name":"Whenever You Need Somebody","images":[{"url":"https://i.scdn.co/image/cover.jpg"}]}}
        """;

    [Theory]
    [InlineData("https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC?si=abc123")]
    [InlineData("https://open.spotify.com/intl-nl/track/4uLU6hMCjMI75M1A2tKUQC")]
    [InlineData("spotify:track:4uLU6hMCjMI75M1A2tKUQC")]
    [InlineData("4uLU6hMCjMI75M1A2tKUQC")]
    public async Task Resolves_every_accepted_input_form_through_the_tracks_endpoint(string input)
    {
        (SpotifyMusicProvider provider, RecordingHttpHandler handler) = Build(connectSpotify: true);
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith($"/tracks/{TrackId}", StringComparison.Ordinal),
            HttpStatusCode.OK,
            TrackJson
        );

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, input);

        track.Should().NotBeNull();
        handler
            .RequestUrls.Should()
            .ContainSingle(url => url.Contains($"/tracks/{TrackId}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Maps_the_track_payload_field_by_field_into_the_normalized_TrackInfo()
    {
        (SpotifyMusicProvider provider, RecordingHttpHandler handler) = Build(connectSpotify: true);
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith($"/tracks/{TrackId}", StringComparison.Ordinal),
            HttpStatusCode.OK,
            TrackJson
        );

        TrackInfo? track = await provider.ResolveTrackAsync(
            ChannelId,
            $"https://open.spotify.com/track/{TrackId}"
        );

        track.Should().NotBeNull();
        track.TrackName.Should().Be("Never Gonna Give You Up");
        track.Artist.Should().Be("Rick Astley, Featured Guest");
        track.Album.Should().Be("Whenever You Need Somebody");
        track.TrackUri.Should().Be($"spotify:track:{TrackId}");
        track.AlbumArtUrl.Should().Be("https://i.scdn.co/image/cover.jpg");
        track.DurationMs.Should().Be(213573);
        track.Provider.Should().Be("spotify");
        track.ProviderTrackId.Should().Be(TrackId);
        track.IsExplicit.Should().BeTrue("the payload flags the track explicit");
        track.IsAgeRestricted.Should().BeFalse("Spotify exposes no age-restriction flag");
        track.IsEmbeddable.Should().BeTrue("no embed constraint applies to Spotify playback");
        track.IsPlaying.Should().BeFalse("a metadata resolve is not a now-playing read");
        track.ProgressMs.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_track_id_resolves_to_null()
    {
        (SpotifyMusicProvider provider, _) = Build(connectSpotify: true);
        // No route registered — the handler answers 404, Spotify's real "no such track".

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, "zzzzzzzzzzzzzzzzzzzzzz");

        track.Should().BeNull();
    }

    [Fact]
    public async Task Unconnected_broadcaster_fails_closed_without_touching_the_API()
    {
        (SpotifyMusicProvider provider, RecordingHttpHandler handler) = Build(
            connectSpotify: false
        );

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, $"spotify:track:{TrackId}");

        track.Should().BeNull();
        handler.RequestUrls.Should().BeEmpty("no token means no API call");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com/track/abc")]
    [InlineData("not a track id!!!")]
    [InlineData("spotify:track:")]
    public async Task Unparseable_input_fails_closed_without_touching_the_API(string input)
    {
        (SpotifyMusicProvider provider, RecordingHttpHandler handler) = Build(connectSpotify: true);

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, input);

        track.Should().BeNull();
        handler.RequestUrls.Should().BeEmpty("garbage input must not become an API request");
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (SpotifyMusicProvider Provider, RecordingHttpHandler Handler) Build(
        bool connectSpotify
    )
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        if (connectSpotify)
        {
            db.Services.Add(
                new Service
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "spotify",
                    BroadcasterId = ChannelId,
                    Enabled = true,
                    AccessToken = "test-access-token",
                }
            );
            db.SaveChanges();
        }

        RecordingHttpHandler handler = new();
        SpotifyMusicProvider provider = new(
            db,
            new PassthroughProtector(),
            new InMemoryIntegrationCapabilityStore(),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance
        );
        return (provider, handler);
    }
}
