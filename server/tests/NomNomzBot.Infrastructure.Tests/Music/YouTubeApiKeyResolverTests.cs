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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.MediaShare;
using NomNomzBot.Infrastructure.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The YouTube Data API key an operator saves in the dashboard has to reach the wire without a restart:
/// the stored value wins, the <c>YouTube:ApiKey</c> config value is the fallback, and both readers
/// (search and the media-share resolver) ask per request.
/// </summary>
public sealed class YouTubeApiKeyResolverTests
{
    private const string StoredKey = "test-key-123";
    private const string ConfigKey = "config-key-456";
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f5002");

    private static YouTubeApiKeyResolver Resolver(string? stored, string? config)
    {
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetValueAsync("youtube", "api_key", Arg.Any<CancellationToken>())
            .Returns(stored);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["YouTube:ApiKey"] = config })
            .Build();
        return new(credentials, configuration);
    }

    [Fact]
    public async Task The_stored_key_wins_over_the_config_key()
    {
        (await Resolver(StoredKey, ConfigKey).GetAsync()).Should().Be(StoredKey);
    }

    [Fact]
    public async Task The_config_key_is_used_when_nothing_is_stored()
    {
        (await Resolver(null, ConfigKey).GetAsync()).Should().Be(ConfigKey);
    }

    [Fact]
    public async Task Neither_source_reads_as_null()
    {
        (await Resolver(null, null).GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Search_sends_a_saved_key_when_no_config_key_is_set()
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/videos", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """{"items":[]}"""
        );
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: null,
            handler: handler,
            keyResolver: Resolver(StoredKey, null)
        );

        (_, MusicProviderFailureReason failure) = await provider.ResolveTrackAsync(
            ChannelId,
            "https://youtu.be/dQw4w9WgXcQ"
        );

        failure.Should().NotBe(MusicProviderFailureReason.NotConfigured);
        handler
            .RequestUrls.Should()
            .Contain(url =>
                url.Contains("/youtube/v3/videos?", StringComparison.Ordinal)
                && url.Contains($"key={StoredKey}", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task Search_with_no_key_anywhere_is_an_outage_not_a_missing_setup_and_never_calls_the_Data_API()
    {
        RecordingHttpHandler handler = new();
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: null,
            handler: handler,
            keyResolver: Resolver(null, null)
        );

        (_, MusicProviderFailureReason failure) = await provider.SearchAsync(ChannelId, "query");

        failure.Should().Be(MusicProviderFailureReason.Unavailable);
        handler
            .RequestUrls.Should()
            .NotContain(url => url.Contains("googleapis.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Media_share_resolution_sends_a_saved_key_when_no_config_key_is_set()
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/videos", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """{"items":[]}"""
        );
        MediaSourceResolver sut = new(
            Substitute.For<ITwitchClipsApi>(),
            new SingleHandlerClientFactory(handler),
            Resolver(StoredKey, null),
            NullLogger<MediaSourceResolver>.Instance
        );

        await sut.ResolveAsync(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            allowTwitchClips: false,
            allowYouTube: true
        );

        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.Contains("/youtube/v3/videos?", StringComparison.Ordinal)
                && url.Contains($"key={StoredKey}", StringComparison.Ordinal)
            );
    }
}
