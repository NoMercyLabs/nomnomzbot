// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// YouTube song requests with no OAuth and no API key (decisions/youtube-without-oauth-or-key.md): keyless
/// oEmbed + public player data + web search go first; the key is only a fallback for the 1-unit
/// videos.list lookup, and search never calls the Data API. Fixtures are trimmed from real answers
/// recorded on 2026-10-17.
/// </summary>
public sealed class YouTubeKeylessTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f6101");
    private const string RickId = "dQw4w9WgXcQ";
    private const string LoopId = "LLFhKaqnWwk";
    private const string LiveId = "jfKfPfyJRdk";
    private const string NoEmbedId = "7FwDP17XPlk";

    private const string RickOEmbed = """
        {"title":"Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster)",
         "author_name":"Rick Astley","type":"video","provider_name":"YouTube",
         "thumbnail_url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg"}
        """;

    private static string PlayerJson(
        string id,
        string title,
        int lengthSeconds,
        string? category = "Music",
        string author = "Rick Astley"
    )
    {
        string categoryPart = category is null ? string.Empty : $$""","category":"{{category}}" """;
        return $$$$"""
            {"playabilityStatus":{"status":"UNPLAYABLE","reason":"Video unavailable"},
             "videoDetails":{"videoId":"{{{{id}}}}","title":"{{{{title}}}}","lengthSeconds":"{{{{lengthSeconds}}}}",
               "author":"{{{{author}}}}","isLiveContent":false},
             "microformat":{"playerMicroformatRenderer":{"availableCountries":["NL","US"],"isFamilySafe":true{{{{categoryPart}}}}}}}
            """;
    }

    private const string TutorialId = "QMQXikJWrCA";

    private static async Task<(
        TrackInfo? Track,
        MusicProviderFailureReason Failure
    )> ResolveKeylessAsync(string id, string? category, string author = "Rick Astley")
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(r => OEmbedFor(r, id), HttpStatusCode.OK, RickOEmbed);
        handler.RespondWhen(
            r => PlayerFor(r, id),
            HttpStatusCode.OK,
            PlayerJson(id, "A video", 213, category, author)
        );
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: null,
            handler: handler
        );
        return await provider.ResolveTrackAsync(ChannelId, $"https://youtu.be/{id}");
    }

    [Fact]
    public async Task A_link_to_a_non_music_video_resolves_tagged_outside_the_music_category()
    {
        (TrackInfo? track, MusicProviderFailureReason failure) = await ResolveKeylessAsync(
            TutorialId,
            "Education"
        );

        failure.Should().Be(MusicProviderFailureReason.None);
        track!.IsMusicCategory.Should().BeFalse();
    }

    [Fact]
    public async Task A_topic_channel_upload_counts_as_music_even_outside_the_music_category()
    {
        (TrackInfo? track, MusicProviderFailureReason failure) = await ResolveKeylessAsync(
            TutorialId,
            "Entertainment",
            "Rick Astley - Topic"
        );

        failure.Should().Be(MusicProviderFailureReason.None);
        track!.ProviderTrackId.Should().Be(TutorialId);
    }

    [Fact]
    public async Task A_player_answer_with_no_category_is_admitted_rather_than_refused()
    {
        (TrackInfo? track, MusicProviderFailureReason failure) = await ResolveKeylessAsync(
            TutorialId,
            null
        );

        failure.Should().Be(MusicProviderFailureReason.None);
        track!.DurationMs.Should().Be(213_000);
    }

    [Fact]
    public async Task A_word_search_tags_each_hit_with_its_music_category()
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(r => Path(r, "/youtubei/v1/search"), HttpStatusCode.OK, SearchJson());
        RouteRick(handler);
        handler.RespondWhen(r => OEmbedFor(r, LoopId), HttpStatusCode.OK, RickOEmbed);
        handler.RespondWhen(
            r => PlayerFor(r, LoopId),
            HttpStatusCode.OK,
            PlayerJson(LoopId, "reaction", 600, "Entertainment")
        );
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: null,
            handler: handler
        );

        (IReadOnlyList<TrackInfo> hits, _) = await provider.SearchAsync(
            ChannelId,
            "never gonna give you up",
            10
        );

        // The provider tags; the channel's setting decides in MusicService.
        hits.Select(h => (h.ProviderTrackId, h.IsMusicCategory))
            .Should()
            .Equal((RickId, true), (LoopId, false));
    }

    [Fact]
    public async Task With_a_key_the_Data_API_fallback_refuses_a_video_outside_category_10()
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(r => OEmbedFor(r, RickId), HttpStatusCode.OK, RickOEmbed);
        handler.RespondWhen(
            r => Path(r, "/youtubei/v1/player"),
            HttpStatusCode.InternalServerError
        );
        handler.RespondWhen(
            r => Path(r, "googleapis.com/youtube/v3/videos"),
            HttpStatusCode.OK,
            """
            {"items":[{"id":"dQw4w9WgXcQ","snippet":{"title":"How to","channelTitle":"Tutor","categoryId":"27",
              "liveBroadcastContent":"none"},"contentDetails":{"duration":"PT3M33S"},"status":{"embeddable":true}}]}
            """
        );
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: "owner-key",
            handler: handler
        );

        (TrackInfo? track, MusicProviderFailureReason failure) = await provider.ResolveTrackAsync(
            ChannelId,
            $"https://youtu.be/{RickId}"
        );

        failure.Should().Be(MusicProviderFailureReason.None);
        track!.IsMusicCategory.Should().BeFalse();
    }

    private static string Renderer(string id, string title, string? length)
    {
        string lengthPart = length is null
            ? string.Empty
            : $$""","lengthText":{"simpleText":"{{length}}"}""";
        return $$$"""{"videoRenderer":{"videoId":"{{{id}}}","title":{"runs":[{"text":"{{{title}}}"}]},"ownerText":{"runs":[{"text":"Rick Astley"}]}{{{lengthPart}}}}}""";
    }

    private static string SearchJson()
    {
        string items = string.Join(
            ",",
            Renderer(LiveId, "lofi hip hop radio live", null),
            Renderer(RickId, "Rick Astley - Never Gonna Give You Up", "3:33"),
            Renderer(NoEmbedId, "Never Gonna Give You Up (lyrics)", "3:34"),
            Renderer(LoopId, "Never Gonna Give You Up 10 hours", "10:00:00")
        );
        const string Head =
            """{"contents":{"twoColumnSearchResultsRenderer":{"primaryContents":{"sectionListRenderer":{"contents":[{"itemSectionRenderer":{"contents":[""";
        const string Tail = """]}}]}}}}}""";
        return Head + items + Tail;
    }

    private static bool Path(HttpRequestMessage request, string part) =>
        request.RequestUri!.AbsoluteUri.Contains(part, StringComparison.Ordinal);

    private static bool OEmbedFor(HttpRequestMessage request, string id) =>
        Path(request, "/oembed") && Path(request, id);

    private static bool PlayerFor(HttpRequestMessage request, string id) =>
        Path(request, "/youtubei/v1/player")
        && request.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Contains(id);

    private static void RouteRick(RecordingHttpHandler handler)
    {
        handler.RespondWhen(r => OEmbedFor(r, RickId), HttpStatusCode.OK, RickOEmbed);
        handler.RespondWhen(
            r => PlayerFor(r, RickId),
            HttpStatusCode.OK,
            PlayerJson(RickId, "Rick Astley - Never Gonna Give You Up", 213)
        );
    }

    private static bool AnyDataApiCall(RecordingHttpHandler handler) =>
        handler.RequestUrls.Any(url => url.Contains("googleapis.com", StringComparison.Ordinal));

    [Fact]
    public async Task With_no_key_and_no_connection_YouTube_is_a_usable_provider()
    {
        IApplicationDbContext db = MusicTestDbContext.New();
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(apiKey: null, db: db);

        IReadOnlyList<IMusicProvider> usable = await UsableMusicProviders.ForChannelAsync(
            db,
            [provider],
            ChannelId,
            CancellationToken.None
        );

        usable.Should().ContainSingle().Which.Provider.Should().Be("youtube");
    }

    [Fact]
    public async Task A_pasted_link_resolves_title_from_oembed_and_length_from_player_data_with_no_key()
    {
        RecordingHttpHandler handler = new();
        RouteRick(handler);
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: null,
            handler: handler
        );

        (TrackInfo? track, MusicProviderFailureReason failure) = await provider.ResolveTrackAsync(
            ChannelId,
            $"https://youtu.be/{RickId}"
        );

        failure.Should().Be(MusicProviderFailureReason.None);
        track.Should().NotBeNull();
        track.ProviderTrackId.Should().Be(RickId);
        track
            .TrackName.Should()
            .Be("Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster)");
        track.Artist.Should().Be("Rick Astley");
        track.DurationMs.Should().Be(213_000);
        track.AlbumArtUrl.Should().Be("https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg");
        track.IsEmbeddable.Should().BeTrue();
        track.IsAgeRestricted.Should().BeFalse();
        AnyDataApiCall(handler).Should().BeFalse();
    }

    [Fact]
    public async Task A_word_search_with_no_key_returns_only_playable_on_demand_hits_and_the_pick_skips_the_loop()
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(r => Path(r, "/youtubei/v1/search"), HttpStatusCode.OK, SearchJson());
        RouteRick(handler);
        handler.RespondWhen(r => OEmbedFor(r, NoEmbedId), HttpStatusCode.Unauthorized);
        handler.RespondWhen(
            r => PlayerFor(r, NoEmbedId),
            HttpStatusCode.OK,
            PlayerJson(NoEmbedId, "lyrics", 214)
        );
        handler.RespondWhen(
            r => OEmbedFor(r, LoopId),
            HttpStatusCode.OK,
            RickOEmbed.Replace("(Official Video) (4K Remaster)", "10 hours")
        );
        handler.RespondWhen(
            r => PlayerFor(r, LoopId),
            HttpStatusCode.OK,
            PlayerJson(LoopId, "Never Gonna Give You Up 10 hours", 36_000)
        );
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: null,
            handler: handler
        );

        (IReadOnlyList<TrackInfo> hits, MusicProviderFailureReason failure) =
            await provider.SearchAsync(ChannelId, "never gonna give you up", 10);

        failure.Should().Be(MusicProviderFailureReason.None);
        hits.Select(h => h.ProviderTrackId).Should().Equal(RickId, LoopId);
        SongSearchRanker.Pick("never gonna give you up", hits)!.ProviderTrackId.Should().Be(RickId);
        AnyDataApiCall(handler).Should().BeFalse();
    }

    [Fact]
    public async Task With_a_key_keyless_still_goes_first_and_search_never_calls_the_Data_API()
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(r => Path(r, "/youtubei/v1/search"), HttpStatusCode.OK, SearchJson());
        RouteRick(handler);
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: "owner-key",
            handler: handler
        );

        (TrackInfo? track, _) = await provider.ResolveTrackAsync(
            ChannelId,
            $"https://youtu.be/{RickId}"
        );
        await provider.SearchAsync(ChannelId, "never gonna give you up", 10);

        track!.DurationMs.Should().Be(213_000);
        // The other hits answer nothing keyless, so the key's 1-unit videos.list may back them up; the
        // 100-unit search.list is never called.
        handler
            .RequestUrls.Should()
            .NotContain(url => url.Contains("/youtube/v3/search", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_a_key_the_Data_API_is_called_only_when_keyless_player_data_fails()
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(r => OEmbedFor(r, RickId), HttpStatusCode.OK, RickOEmbed);
        handler.RespondWhen(
            r => Path(r, "/youtubei/v1/player"),
            HttpStatusCode.InternalServerError
        );
        handler.RespondWhen(
            r => Path(r, "googleapis.com/youtube/v3/videos"),
            HttpStatusCode.OK,
            """
            {"items":[{"id":"dQw4w9WgXcQ","snippet":{"title":"From the Data API","channelTitle":"Rick Astley",
              "liveBroadcastContent":"none"},"contentDetails":{"duration":"PT3M33S"},"status":{"embeddable":true}}]}
            """
        );
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: "owner-key",
            handler: handler
        );

        (TrackInfo? track, MusicProviderFailureReason failure) = await provider.ResolveTrackAsync(
            ChannelId,
            $"https://youtu.be/{RickId}"
        );

        failure.Should().Be(MusicProviderFailureReason.None);
        track!.TrackName.Should().Be("From the Data API");
        track.DurationMs.Should().Be(213_000);
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.Contains("googleapis.com/youtube/v3/videos", StringComparison.Ordinal)
            );
        handler
            .RequestUrls.Should()
            .NotContain(url => url.Contains("/youtube/v3/search", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_keyless_failure_with_no_key_is_a_typed_outage_never_not_connected()
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(r => Path(r, "/youtubei/"), HttpStatusCode.InternalServerError);
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: null,
            handler: handler
        );

        (TrackInfo? track, MusicProviderFailureReason resolveFailure) =
            await provider.ResolveTrackAsync(ChannelId, $"https://youtu.be/{RickId}");
        (IReadOnlyList<TrackInfo> hits, MusicProviderFailureReason searchFailure) =
            await provider.SearchAsync(ChannelId, "never gonna give you up", 10);

        track.Should().BeNull();
        hits.Should().BeEmpty();
        resolveFailure.Should().Be(MusicProviderFailureReason.Unavailable);
        searchFailure.Should().Be(MusicProviderFailureReason.Unavailable);
        AnyDataApiCall(handler).Should().BeFalse();
    }
}
