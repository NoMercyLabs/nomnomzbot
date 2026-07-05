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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves §3.5's YouTube <c>ResolveTrackAsync</c>: every accepted link form (watch?v=, youtu.be,
/// music/m.youtube, /shorts, bare id, with extra &amp;t=/&amp;list= params) extracts the same video id
/// and resolves through videos.list into a mapped <see cref="TrackInfo"/>; a found on-demand video
/// keeps its embeddable/age gate flags for the SR pipeline; a live broadcast, an unknown/blocked id,
/// garbage input, and an unconfigured key all fail closed to null — never a throw, never a stray call.
/// ISO-8601 durations parse into milliseconds.
/// </summary>
public sealed class YouTubeMusicProviderResolveTrackTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f6001");
    private const string ApiKey = "test-key";
    private const string VideoId = "dQw4w9WgXcQ";

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abcDEF123")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLabcd&index=3")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ")]
    public async Task Resolves_every_accepted_input_form_to_the_same_video_id(string input)
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteVideos(handler, OneVideoJson("PT4M13S"));

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, input);

        track.Should().NotBeNull();
        track!.ProviderTrackId.Should().Be(VideoId);
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.Contains($"id={VideoId}", StringComparison.Ordinal)
                && url.Contains("part=snippet,contentDetails,status", StringComparison.Ordinal)
                && url.Contains($"key={ApiKey}", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task Resolve_maps_the_video_payload_field_by_field()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteVideos(
            handler,
            """
            {"items":[{"id":"dQw4w9WgXcQ",
              "snippet":{"title":"Never Gonna Give You Up","channelTitle":"Rick Astley",
                "liveBroadcastContent":"none",
                "thumbnails":{"medium":{"url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/medium.jpg"},
                  "high":{"url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/high.jpg"}}},
              "contentDetails":{"duration":"PT4M13S"},
              "status":{"embeddable":true}}]}
            """
        );

        TrackInfo? track = await provider.ResolveTrackAsync(
            ChannelId,
            "https://youtu.be/dQw4w9WgXcQ"
        );

        track.Should().NotBeNull();
        track!.TrackName.Should().Be("Never Gonna Give You Up");
        track.Artist.Should().Be("Rick Astley");
        track.Album.Should().BeEmpty();
        track.TrackUri.Should().Be("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        track.AlbumArtUrl.Should().Be("https://i.ytimg.com/vi/dQw4w9WgXcQ/high.jpg");
        track.DurationMs.Should().Be(253000);
        track.Provider.Should().Be("youtube");
        track.ProviderTrackId.Should().Be(VideoId);
        track.IsExplicit.Should().BeFalse();
        track.IsAgeRestricted.Should().BeFalse();
        track.IsEmbeddable.Should().BeTrue();
    }

    [Fact]
    public async Task Non_embeddable_video_resolves_with_the_flag_preserved_for_the_pipeline_gate()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteVideos(
            handler,
            """
            {"items":[{"id":"dQw4w9WgXcQ","snippet":{"title":"Locked","channelTitle":"Label","liveBroadcastContent":"none","thumbnails":{}},"contentDetails":{"duration":"PT3M0S"},"status":{"embeddable":false}}]}
            """
        );

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, VideoId);

        track
            .Should()
            .NotBeNull(
                "resolve returns the flags so the SR pipeline rejects with the precise reason, not a bare not-found"
            );
        track!.IsEmbeddable.Should().BeFalse();
    }

    [Fact]
    public async Task Age_restricted_video_resolves_with_the_flag_preserved_for_the_pipeline_gate()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteVideos(
            handler,
            """
            {"items":[{"id":"dQw4w9WgXcQ","snippet":{"title":"Mature","channelTitle":"Chan","liveBroadcastContent":"none","thumbnails":{}},"contentDetails":{"duration":"PT3M0S","contentRating":{"ytRating":"ytAgeRestricted"}},"status":{"embeddable":true}}]}
            """
        );

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, VideoId);

        track.Should().NotBeNull();
        track!.IsAgeRestricted.Should().BeTrue();
    }

    [Fact]
    public async Task Live_broadcast_resolves_to_null()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteVideos(
            handler,
            """
            {"items":[{"id":"dQw4w9WgXcQ","snippet":{"title":"LIVE","channelTitle":"News","liveBroadcastContent":"live","thumbnails":{}},"contentDetails":{"duration":"PT0S"},"status":{"embeddable":true}}]}
            """
        );

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, VideoId);

        track.Should().BeNull("a live broadcast is not a resolvable on-demand track");
    }

    [Fact]
    public async Task Unknown_or_blocked_id_resolves_to_null()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        // Unknown/private/deleted/region-blocked videos all come back as an empty item set.
        RouteVideos(handler, """{"items":[]}""");

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, VideoId);

        track.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a video id")]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://vimeo.com/123456789")]
    [InlineData("https://www.youtube.com/watch?v=tooShort")]
    [InlineData("https://www.youtube.com/watch?v=waaaaaaytoolong")]
    public async Task Garbage_input_resolves_to_null_without_any_http_call(string input)
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, input);

        track.Should().BeNull();
        handler.RequestUrls.Should().BeEmpty("garbage input must not become an API request");
    }

    [Fact]
    public async Task Unconfigured_api_key_resolves_to_null_without_any_http_call()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build(apiKey: null);

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, VideoId);

        track.Should().BeNull();
        handler.RequestUrls.Should().BeEmpty("an unconfigured key must not reach the Data API");
    }

    [Theory]
    [InlineData("PT1H2M3S", 3723000)]
    [InlineData("PT30S", 30000)]
    [InlineData("PT4M13S", 253000)]
    [InlineData("PT0S", 0)]
    public async Task Resolve_parses_iso8601_duration_into_milliseconds(string iso, int expectedMs)
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteVideos(handler, OneVideoJson(iso));

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, VideoId);

        track.Should().NotBeNull();
        track!.DurationMs.Should().Be(expectedMs);
    }

    [Fact]
    public async Task Missing_duration_maps_to_zero_milliseconds()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteVideos(handler, OneVideoJson(duration: null));

        TrackInfo? track = await provider.ResolveTrackAsync(ChannelId, VideoId);

        track.Should().NotBeNull();
        track!.DurationMs.Should().Be(0);
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (YouTubeMusicProvider Provider, RecordingHttpHandler Handler) Build(
        string? apiKey = ApiKey
    )
    {
        RecordingHttpHandler handler = new();
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(apiKey, handler);
        return (provider, handler);
    }

    private static void RouteVideos(RecordingHttpHandler handler, string json) =>
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/videos", StringComparison.Ordinal),
            HttpStatusCode.OK,
            json
        );

    /// <summary>A one-video videos.list body; a null <paramref name="duration"/> omits the field.</summary>
    private static string OneVideoJson(string? duration)
    {
        string contentDetails = duration is null ? "{}" : "{\"duration\":\"" + duration + "\"}";

        return "{\"items\":[{\"id\":\""
            + VideoId
            + "\",\"snippet\":{\"title\":\"Never Gonna Give You Up\","
            + "\"channelTitle\":\"Rick Astley\",\"liveBroadcastContent\":\"none\","
            + "\"thumbnails\":{}},\"contentDetails\":"
            + contentDetails
            + ",\"status\":{\"embeddable\":true}}]}";
    }
}
