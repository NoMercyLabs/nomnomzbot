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
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Infrastructure.Community;
using NomNomzBot.Infrastructure.Tests.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Community;

/// <summary>
/// Stoney, 2026-11-10: "we need analytics and all those features we have from twitch to match". A Twitch clip
/// goes to the streamer's YouTube channel: the clip's download is streamed into <c>videos.insert</c> (resumable
/// upload, read against the live docs 2026-11-10), private unless the streamer chooses otherwise.
/// </summary>
public sealed class YouTubeClipUploadServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000e3be7");
    private const string ClipId = "FunnyClip-abc";
    private const string DownloadUrl = "https://clips.example/funny.mp4";
    private const string SessionUrl =
        "https://www.googleapis.com/upload/youtube/v3/videos?upload_id=s1";
    private static readonly byte[] ClipBytes = Encoding.ASCII.GetBytes("the-bytes-of-the-clip");

    private readonly UploadHandler _http = new();
    private readonly ITwitchClipsApi _clips = Substitute.For<ITwitchClipsApi>();
    private readonly IYouTubeAccessTokenProvider _tokens =
        Substitute.For<IYouTubeAccessTokenProvider>();
    private readonly FakeCache _cache = new();

    // 2026-11-10 20:00 UTC is 2026-11-10 12:00 in Google's day (Pacific time).
    private readonly FakeTimeProvider _time = new(new(2026, 11, 10, 20, 0, 0, TimeSpan.Zero));

    public YouTubeClipUploadServiceTests()
    {
        _tokens.GetAccessTokenAsync(Channel, Arg.Any<CancellationToken>()).Returns("token-1");
        _clips
            .GetClipsByIdsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.Single() == ClipId),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchClip>>([Clip()]));
        _clips
            .GetClipDownloadUrlsAsync(
                Channel,
                "555001",
                Arg.Is<IReadOnlyList<string>>(ids => ids.Single() == ClipId),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchClipDownload>>([
                    new TwitchClipDownload(ClipId, DownloadUrl, null),
                ])
            );
    }

    private static TwitchClip Clip() =>
        new(
            ClipId,
            "https://clips.twitch.tv/FunnyClip-abc",
            "https://clips.twitch.tv/embed?clip=FunnyClip-abc",
            "555001",
            "SampleStreamer",
            "100000042",
            "SampleViewer",
            "v1",
            "g1",
            "en",
            "The funny moment",
            12,
            new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero),
            "https://clips.example/thumb.jpg",
            28.5,
            null,
            false
        );

    private YouTubeClipUploadService Sut() =>
        new(
            new SingleHandlerClientFactory(_http),
            _tokens,
            _clips,
            _cache,
            _time,
            NullLogger<YouTubeClipUploadService>.Instance
        );

    private static YouTubeClipUploadRequest Words(string? privacy = null) =>
        new("The funny moment", "Clipped live on Twitch.", privacy);

    [Fact]
    public async Task The_clip_is_streamed_into_the_upload_and_the_result_holds_the_new_video_id()
    {
        Result<YouTubeClipUploadDto> result = await Sut().UploadAsync(Channel, ClipId, Words());

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Should()
            .Be(
                new YouTubeClipUploadDto(
                    YouTubeClipUploadOutcome.Uploaded,
                    "yt-video-1",
                    "https://www.youtube.com/watch?v=yt-video-1",
                    99
                )
            );

        _http
            .Requests.Select(r => $"{r.Method} {r.Url}")
            .Should()
            .Equal(
                $"GET {DownloadUrl}",
                "POST https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status",
                $"PUT {SessionUrl}"
            );

        Recorded start = _http.Requests[1];
        start.Authorization.Should().Be("Bearer token-1");
        start.Headers["X-Upload-Content-Length"].Should().Be(ClipBytes.Length.ToString());
        start.Headers["X-Upload-Content-Type"].Should().Be("video/mp4");
        JsonElement words = JsonDocument.Parse(start.Body).RootElement;
        words
            .GetProperty("snippet")
            .GetProperty("title")
            .GetString()
            .Should()
            .Be("The funny moment");
        words
            .GetProperty("snippet")
            .GetProperty("description")
            .GetString()
            .Should()
            .Be("Clipped live on Twitch.");
        words
            .GetProperty("status")
            .GetProperty("privacyStatus")
            .GetString()
            .Should()
            .Be("private", "a video is private unless the streamer chooses otherwise");

        Recorded upload = _http.Requests[2];
        upload.Authorization.Should().Be("Bearer token-1");
        upload.ContentType.Should().Be("video/mp4");
        upload.Bytes.Should().Equal(ClipBytes, "the clip's own bytes reach YouTube unchanged");
        _http
            .Requests[0]
            .Authorization.Should()
            .BeNull("the YouTube token never goes to the clip host");
    }

    [Theory]
    [InlineData("unlisted")]
    [InlineData("public")]
    public async Task The_privacy_the_streamer_chose_is_the_privacy_of_the_video(string privacy)
    {
        await Sut().UploadAsync(Channel, ClipId, Words(privacy));

        JsonDocument
            .Parse(_http.Requests[1].Body)
            .RootElement.GetProperty("status")
            .GetProperty("privacyStatus")
            .GetString()
            .Should()
            .Be(privacy);
    }

    [Theory]
    [InlineData("", "words", null)]
    [InlineData("a title", "words", "friends-only")]
    public async Task Words_YouTube_would_refuse_are_a_validation_failure_and_nothing_is_sent(
        string title,
        string description,
        string? privacy
    )
    {
        Result<YouTubeClipUploadDto> result = await Sut()
            .UploadAsync(Channel, ClipId, new(title, description, privacy));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_title_over_100_characters_is_a_validation_failure()
    {
        Result<YouTubeClipUploadDto> result = await Sut()
            .UploadAsync(Channel, ClipId, new(new string('a', 101), null, null));

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task A_channel_without_a_YouTube_connection_is_told_so_and_no_clip_is_fetched()
    {
        _tokens.GetAccessTokenAsync(Channel, Arg.Any<CancellationToken>()).Returns((string?)null);

        Result<YouTubeClipUploadDto> result = await Sut().UploadAsync(Channel, ClipId, Words());

        result.Value.Outcome.Should().Be(YouTubeClipUploadOutcome.NotConnected);
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_clip_Twitch_gives_no_download_for_is_told_so_and_YouTube_is_not_asked()
    {
        _clips
            .GetClipDownloadUrlsAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<IReadOnlyList<TwitchClipDownload>>("missing scope", "MISSING_SCOPE")
            );

        Result<YouTubeClipUploadDto> result = await Sut().UploadAsync(Channel, ClipId, Words());

        result.Value.Outcome.Should().Be(YouTubeClipUploadOutcome.ClipUnavailable);
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_grant_YouTube_refuses_is_told_so_and_no_clip_bytes_are_sent()
    {
        _http.StartStatus = HttpStatusCode.Forbidden;

        Result<YouTubeClipUploadDto> result = await Sut().UploadAsync(Channel, ClipId, Words());

        result.Value.Outcome.Should().Be(YouTubeClipUploadOutcome.NotAllowed);
        _http.Requests.Should().NotContain(r => r.Method == "PUT");
        result.Value.UploadsLeftToday.Should().Be(100, "a refused upload does not count");
    }

    [Fact]
    public async Task The_upload_limit_YouTube_reports_is_told_as_the_limit_not_as_an_error()
    {
        _http.StartStatus = HttpStatusCode.BadRequest;
        _http.StartBody = """{"error":{"code":400,"errors":[{"reason":"uploadLimitExceeded"}]}}""";

        Result<YouTubeClipUploadDto> result = await Sut().UploadAsync(Channel, ClipId, Words());

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(YouTubeClipUploadOutcome.LimitReached);
    }

    [Fact]
    public async Task The_hundredth_upload_of_the_day_is_the_last_and_the_next_one_asks_nobody()
    {
        YouTubeClipUploadService sut = Sut();
        for (int i = 0; i < 100; i++)
            (await sut.UploadAsync(Channel, ClipId, Words())).Value.Outcome.Should().Be("uploaded");
        (await sut.UploadsLeftTodayAsync()).Should().Be(0);
        _http.Requests.Clear();

        Result<YouTubeClipUploadDto> refused = await sut.UploadAsync(Channel, ClipId, Words());

        refused.Value.Outcome.Should().Be(YouTubeClipUploadOutcome.LimitReached);
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task The_count_starts_again_when_Googles_day_turns_at_midnight_Pacific_time()
    {
        YouTubeClipUploadService sut = Sut();
        await sut.UploadAsync(Channel, ClipId, Words());
        (await sut.UploadsLeftTodayAsync()).Should().Be(99);

        // 2026-11-11 07:59 UTC is still 2026-11-10 in Pacific time; 08:01 UTC is the next day.
        _time.SetUtcNow(new(2026, 11, 11, 7, 59, 0, TimeSpan.Zero));
        (await sut.UploadsLeftTodayAsync()).Should().Be(99);
        _time.SetUtcNow(new(2026, 11, 11, 8, 1, 0, TimeSpan.Zero));
        (await sut.UploadsLeftTodayAsync()).Should().Be(100);
    }

    [Fact]
    public async Task A_YouTube_outage_during_the_upload_is_a_failure_and_does_not_count()
    {
        _http.UploadStatus = HttpStatusCode.ServiceUnavailable;
        YouTubeClipUploadService sut = Sut();

        Result<YouTubeClipUploadDto> result = await sut.UploadAsync(Channel, ClipId, Words());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
        (await sut.UploadsLeftTodayAsync()).Should().Be(100);
    }

    private sealed record Recorded(
        string Method,
        string Url,
        string? Authorization,
        string? ContentType,
        IReadOnlyDictionary<string, string> Headers,
        string Body,
        byte[] Bytes
    );

    /// <summary>Plays the clip host and YouTube's resumable upload, and keeps every request with its bytes.</summary>
    private sealed class UploadHandler : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];
        public HttpStatusCode StartStatus { get; set; } = HttpStatusCode.OK;
        public string? StartBody { get; set; }
        public HttpStatusCode UploadStatus { get; set; } = HttpStatusCode.Created;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            byte[] bytes = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(
                new(
                    request.Method.Method,
                    request.RequestUri!.ToString(),
                    request.Headers.Authorization?.ToString(),
                    request.Content?.Headers.ContentType?.MediaType,
                    request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                    Encoding.UTF8.GetString(bytes),
                    bytes
                )
            );

            if (request.Method == HttpMethod.Get)
            {
                ByteArrayContent clip = new(ClipBytes);
                clip.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
                return new(HttpStatusCode.OK) { Content = clip };
            }

            if (request.Method == HttpMethod.Post)
            {
                HttpResponseMessage start = new(StartStatus);
                if (StartStatus == HttpStatusCode.OK)
                    start.Headers.Location = new(SessionUrl);
                if (StartBody is not null)
                    start.Content = new StringContent(StartBody, Encoding.UTF8, "application/json");
                return start;
            }

            HttpResponseMessage done = new(UploadStatus);
            if (UploadStatus == HttpStatusCode.Created)
                done.Content = new StringContent(
                    """{"kind":"youtube#video","id":"yt-video-1"}""",
                    Encoding.UTF8,
                    "application/json"
                );
            return done;
        }
    }

    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object?> _store = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out object? value) ? (T?)value : default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiry = null,
            CancellationToken ct = default
        )
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.ContainsKey(key));
    }
}
