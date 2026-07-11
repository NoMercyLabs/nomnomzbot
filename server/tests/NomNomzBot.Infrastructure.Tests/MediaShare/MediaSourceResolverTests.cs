// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Infrastructure.MediaShare;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.MediaShare;

/// <summary>
/// Proves the source resolver's URL allowlisting + Twitch-clip metadata mapping (media-share.md D2): the
/// closed set of Twitch-clip and YouTube URL shapes parse to the right ref, a disallowed source or an
/// arbitrary URL is rejected <c>SOURCE_NOT_ALLOWED</c>, and a Twitch clip's real duration/title/thumb flow
/// through from Get Clips. (The YouTube HTTP path's happy case is covered by the live wire; here the
/// no-key branch proves the id parsed and the YouTube branch was taken.)
/// </summary>
public sealed class MediaSourceResolverTests
{
    private static MediaSourceResolver Build(
        ITwitchClipsApi? clips = null,
        string? youTubeApiKey = null
    )
    {
        ITwitchClipsApi clipsApi =
            clips
            ?? BuildClipsApi(
                new TwitchClip(
                    "Abc",
                    "url",
                    "embed",
                    "b1",
                    "bn",
                    "c1",
                    "cn",
                    "v1",
                    "g1",
                    "en",
                    "Clip title",
                    5,
                    default,
                    "https://thumb",
                    12.4,
                    null,
                    false
                )
            );

        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("youtube").Returns(new HttpClient());

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["YouTube:ApiKey"] = youTubeApiKey }
            )
            .Build();

        return new MediaSourceResolver(
            clipsApi,
            factory,
            config,
            NullLogger<MediaSourceResolver>.Instance
        );
    }

    private static ITwitchClipsApi BuildClipsApi(params TwitchClip[] clips)
    {
        ITwitchClipsApi api = Substitute.For<ITwitchClipsApi>();
        api.GetClipsByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchClip>>(clips));
        return api;
    }

    [Theory]
    [InlineData("https://clips.twitch.tv/CleverClipSlug", "CleverClipSlug")]
    [InlineData("https://www.twitch.tv/somestreamer/clip/CleverClipSlug", "CleverClipSlug")]
    [InlineData("https://m.twitch.tv/clip/CleverClipSlug", "CleverClipSlug")]
    public async Task TwitchClipUrls_ParseToTheSlug_AndCarryMetadata(
        string url,
        string expectedSlug
    )
    {
        ITwitchClipsApi api = BuildClipsApi(
            new TwitchClip(
                expectedSlug,
                "url",
                "embed",
                "b",
                "bn",
                "c",
                "cn",
                "v",
                "g",
                "en",
                "Funny",
                1,
                default,
                "https://thumb",
                8.9,
                null,
                false
            )
        );
        MediaSourceResolver sut = Build(api);

        Result<ResolvedMedia> result = await sut.ResolveAsync(url, true, true);

        result.IsSuccess.Should().BeTrue();
        result.Value.SourceType.Should().Be(MediaShareSourceType.TwitchClip);
        result.Value.MediaRef.Should().Be(expectedSlug);
        result.Value.Title.Should().Be("Funny");
        result.Value.DurationSeconds.Should().Be(9); // 8.9 rounds up
        result.Value.ThumbnailUrl.Should().Be("https://thumb");
        await api.Received()
            .GetClipsByIdsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == expectedSlug),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TwitchClip_WhenSourceDisabled_RejectsSourceNotAllowed()
    {
        MediaSourceResolver sut = Build();

        Result<ResolvedMedia> result = await sut.ResolveAsync(
            "https://clips.twitch.tv/Slug",
            allowTwitchClips: false,
            allowYouTube: true
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SOURCE_NOT_ALLOWED");
    }

    [Fact]
    public async Task TwitchClip_NotFound_ReturnsNotFound()
    {
        MediaSourceResolver sut = Build(BuildClipsApi()); // empty result

        Result<ResolvedMedia> result = await sut.ResolveAsync(
            "https://clips.twitch.tv/Missing",
            true,
            true
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Theory]
    [InlineData("https://example.com/whatever")]
    [InlineData("not a url at all")]
    [InlineData("https://vimeo.com/12345")]
    [InlineData("")]
    public async Task ArbitraryUrl_IsRejected(string url)
    {
        MediaSourceResolver sut = Build();

        Result<ResolvedMedia> result = await sut.ResolveAsync(url, true, true);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().BeOneOf("SOURCE_NOT_ALLOWED", "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    public async Task YouTubeUrls_ParseAndReachTheYouTubeBranch(string url)
    {
        // No API key configured → the YouTube branch fails SERVICE_UNAVAILABLE, which proves the id was
        // parsed and routed to YouTube (an unparsed URL would be SOURCE_NOT_ALLOWED instead).
        MediaSourceResolver sut = Build(youTubeApiKey: null);

        Result<ResolvedMedia> result = await sut.ResolveAsync(url, true, true);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task YouTube_WhenSourceDisabled_RejectsSourceNotAllowed()
    {
        MediaSourceResolver sut = Build();

        Result<ResolvedMedia> result = await sut.ResolveAsync(
            "https://youtu.be/dQw4w9WgXcQ",
            allowTwitchClips: true,
            allowYouTube: false
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SOURCE_NOT_ALLOWED");
    }
}
