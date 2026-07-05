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
/// Proves §3.5.2's YouTube <c>SearchAsync</c>: a search.list hit is re-read through videos.list
/// (search.list alone lacks duration/embeddable/age), mapped field-by-field into <see cref="TrackInfo"/>,
/// filtered down to what can actually play in the browser-source player (embeddable, on-demand, not
/// age-restricted), and returned in search-relevance order — with the app <c>YouTube:ApiKey</c> riding
/// the query string and an unconfigured key degrading to empty without a single call.
/// </summary>
public sealed class YouTubeMusicProviderSearchTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f5001");
    private const string ApiKey = "test-key";

    [Fact]
    public async Task Search_maps_the_video_payload_field_by_field_and_carries_the_app_key()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteSearch(handler, """{"items":[{"id":{"videoId":"dQw4w9WgXcQ"}}]}""");
        RouteVideos(
            handler,
            """
            {"items":[{"id":"dQw4w9WgXcQ",
              "snippet":{"title":"Never Gonna Give You Up","channelTitle":"Rick Astley",
                "liveBroadcastContent":"none",
                "thumbnails":{"default":{"url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/default.jpg"},
                  "medium":{"url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/medium.jpg"},
                  "high":{"url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/high.jpg"}}},
              "contentDetails":{"duration":"PT4M13S"},
              "status":{"embeddable":true}}]}
            """
        );

        IReadOnlyList<TrackInfo> results = await provider.SearchAsync(
            ChannelId,
            "never gonna give",
            5
        );

        results.Should().HaveCount(1);
        TrackInfo track = results[0];
        track.TrackName.Should().Be("Never Gonna Give You Up");
        track.Artist.Should().Be("Rick Astley", "the channel is mapped as the artist");
        track.Album.Should().BeEmpty("YouTube has no album concept");
        track.DurationMs.Should().Be(253000, "PT4M13S is 4m13s");
        track.Provider.Should().Be("youtube");
        track.ProviderTrackId.Should().Be("dQw4w9WgXcQ");
        track.TrackUri.Should().Be("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        track.AlbumArtUrl.Should().Be("https://i.ytimg.com/vi/dQw4w9WgXcQ/high.jpg");
        track.IsEmbeddable.Should().BeTrue();
        track.IsAgeRestricted.Should().BeFalse();
        track.IsExplicit.Should().BeFalse("the Data API exposes no explicit-lyrics flag");

        handler
            .RequestUrls.Should()
            .Contain(url =>
                url.Contains("/youtube/v3/search?", StringComparison.Ordinal)
                && url.Contains("part=snippet", StringComparison.Ordinal)
                && url.Contains("type=video", StringComparison.Ordinal)
                && url.Contains("videoEmbeddable=true", StringComparison.Ordinal)
                && url.Contains("maxResults=5", StringComparison.Ordinal)
                // RequestUri.ToString() unescapes for display, so the escaped %20 the provider
                // sends renders as a space here — the assertion proves the full query rode along.
                && url.Contains("q=never gonna give", StringComparison.Ordinal)
                && url.Contains($"key={ApiKey}", StringComparison.Ordinal)
            );

        handler
            .RequestUrls.Should()
            .Contain(url =>
                url.Contains("/youtube/v3/videos?", StringComparison.Ordinal)
                && url.Contains("part=snippet,contentDetails,status", StringComparison.Ordinal)
                && url.Contains("id=dQw4w9WgXcQ", StringComparison.Ordinal)
                && url.Contains($"key={ApiKey}", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task Search_excludes_non_embeddable_age_restricted_and_live_results()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteSearch(
            handler,
            """
            {"items":[
              {"id":{"videoId":"playableAAA"}},
              {"id":{"videoId":"liveBBBBBBB"}},
              {"id":{"videoId":"ageCCCCCCCC"}},
              {"id":{"videoId":"noEmbedDDDD"}}]}
            """
        );
        RouteVideos(
            handler,
            """
            {"items":[
              {"id":"playableAAA","snippet":{"title":"Playable","channelTitle":"Chan","liveBroadcastContent":"none","thumbnails":{}},"contentDetails":{"duration":"PT3M0S"},"status":{"embeddable":true}},
              {"id":"liveBBBBBBB","snippet":{"title":"Live Now","channelTitle":"News","liveBroadcastContent":"live","thumbnails":{}},"contentDetails":{"duration":"PT0S"},"status":{"embeddable":true}},
              {"id":"ageCCCCCCCC","snippet":{"title":"Mature","channelTitle":"Chan","liveBroadcastContent":"none","thumbnails":{}},"contentDetails":{"duration":"PT2M0S","contentRating":{"ytRating":"ytAgeRestricted"}},"status":{"embeddable":true}},
              {"id":"noEmbedDDDD","snippet":{"title":"Locked","channelTitle":"Label","liveBroadcastContent":"none","thumbnails":{}},"contentDetails":{"duration":"PT2M50S"},"status":{"embeddable":false}}]}
            """
        );

        IReadOnlyList<TrackInfo> results = await provider.SearchAsync(ChannelId, "mix", 10);

        results.Should().ContainSingle().Which.ProviderTrackId.Should().Be("playableAAA");
        results
            .Should()
            .NotContain(t => t.ProviderTrackId == "liveBBBBBBB", "live broadcasts are excluded");
        results
            .Should()
            .NotContain(
                t => t.ProviderTrackId == "ageCCCCCCCC",
                "age-restricted videos cannot play embedded"
            );
        results
            .Should()
            .NotContain(
                t => t.ProviderTrackId == "noEmbedDDDD",
                "non-embeddable videos cannot render in the browser source"
            );
    }

    [Fact]
    public async Task Search_preserves_relevance_order_regardless_of_videos_list_order()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteSearch(
            handler,
            """
            {"items":[{"id":{"videoId":"firstAAAAAA"}},{"id":{"videoId":"secondBBBBB"}},{"id":{"videoId":"thirdCCCCCC"}}]}
            """
        );
        // videos.list intentionally returns the trio in a DIFFERENT order than search relevance.
        RouteVideos(
            handler,
            """
            {"items":[
              {"id":"thirdCCCCCC","snippet":{"title":"Third","channelTitle":"C","liveBroadcastContent":"none","thumbnails":{}},"contentDetails":{"duration":"PT1M0S"},"status":{"embeddable":true}},
              {"id":"firstAAAAAA","snippet":{"title":"First","channelTitle":"A","liveBroadcastContent":"none","thumbnails":{}},"contentDetails":{"duration":"PT1M0S"},"status":{"embeddable":true}},
              {"id":"secondBBBBB","snippet":{"title":"Second","channelTitle":"B","liveBroadcastContent":"none","thumbnails":{}},"contentDetails":{"duration":"PT1M0S"},"status":{"embeddable":true}}]}
            """
        );

        IReadOnlyList<TrackInfo> results = await provider.SearchAsync(ChannelId, "q", 5);

        results
            .Select(t => t.ProviderTrackId)
            .Should()
            .Equal("firstAAAAAA", "secondBBBBB", "thirdCCCCCC");
    }

    [Fact]
    public async Task Search_respects_the_requested_maxResults()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();
        RouteSearch(handler, """{"items":[]}""");

        await provider.SearchAsync(ChannelId, "anything", 3);

        handler
            .RequestUrls.Should()
            .Contain(url => url.Contains("maxResults=3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unconfigured_api_key_returns_empty_without_any_http_call()
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build(apiKey: null);

        IReadOnlyList<TrackInfo> results = await provider.SearchAsync(ChannelId, "never gonna", 5);

        results.Should().BeEmpty();
        handler.RequestUrls.Should().BeEmpty("an unconfigured key must not reach the Data API");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_query_returns_empty_without_any_http_call(string query)
    {
        (YouTubeMusicProvider provider, RecordingHttpHandler handler) = Build();

        IReadOnlyList<TrackInfo> results = await provider.SearchAsync(ChannelId, query, 5);

        results.Should().BeEmpty();
        handler.RequestUrls.Should().BeEmpty();
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

    private static void RouteSearch(RecordingHttpHandler handler, string json) =>
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            json
        );

    private static void RouteVideos(RecordingHttpHandler handler, string json) =>
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/videos", StringComparison.Ordinal),
            HttpStatusCode.OK,
            json
        );
}
