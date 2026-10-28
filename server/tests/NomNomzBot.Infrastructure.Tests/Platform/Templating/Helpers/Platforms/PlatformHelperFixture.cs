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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.YouTube;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Platforms;

/// <summary>
/// The catalog section 5 platforms: Twitch 42, Kick 10 and YouTube 8 viewers (60 in total), the three of them
/// connected and live, the fakes standing at the service interfaces.
/// </summary>
internal sealed class PlatformHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid KickChannel = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    public static readonly Guid YouTubeChannel = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LiveSince = new(2026, 10, 26, 12, 15, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public PlatformHelperFixture(PlatformWorld? world = null)
    {
        PlatformWorld set = world ?? new();
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(ChannelA).Returns(Context(set.StreamIsOn));

        Directory = Substitute.For<IPlatformDirectory>();
        Directory
            .ListAsync(Arg.Any<IServiceProvider>(), ChannelA, Arg.Any<CancellationToken>())
            .Returns(set.Links ?? AllThree());

        KickTokens = Substitute.For<IKickAccessTokenProvider>();
        KickTokens
            .GetAsync(KickChannel, Arg.Any<CancellationToken>())
            .Returns(new KickAccess("kick-token", 5555));
        Kick = Substitute.For<IKickApiClient>();
        Kick.GetChannelAsync("kick-token", 5555, Arg.Any<CancellationToken>())
            .Returns(
                set.KickReadFails
                    ? Result.Failure<KickChannel>("down", "UNAVAILABLE")
                    : Result.Success(
                        new KickChannel(5555, "Kick bot night", "Chatting", 7, true, 10)
                    )
            );

        YouTubeSessions = new YouTubeLiveChatSessionRegistry();
        if (set.YouTubeIsLive)
            YouTubeSessions.SetLive(YouTubeChannel, ChannelA, "chat1");
        IYouTubeAccessTokenProvider youTubeTokens = Substitute.For<IYouTubeAccessTokenProvider>();
        youTubeTokens
            .GetAccessTokenAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns("yt-token");
        YouTube = Substitute.For<IYouTubeLiveChatClient>();
        YouTube
            .GetActiveLiveChatsAsync("yt-token", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<YouTubeActiveChat>>([
                    new YouTubeActiveChat("sample", "chat1", "Building a bot", 8),
                ])
            );

        Twitch = Substitute.For<ITwitchChannelsApi>();
        Twitch
            .GetChannelFollowerCountAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(Result.Success(1234));

        Analytics = Substitute.For<IChannelAnalyticsService>();
        Analytics
            .GetPlatformSummaryAsync(
                ChannelA,
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<ChannelAnalyticsPlatformSummaryDto>>(
                    set.Summaries ?? Summaries()
                )
            );

        _services = new ServiceCollection()
            .AddSingleton(Directory)
            .AddSingleton(KickTokens)
            .AddSingleton(Kick)
            .AddSingleton<IYouTubeLiveChatSessionRegistry>(YouTubeSessions)
            .AddSingleton(youTubeTokens)
            .AddSingleton(YouTube)
            .AddSingleton(Twitch)
            .AddSingleton(Analytics)
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        Registry = _services.GetRequiredService<ITemplateHelperRegistry>();
        _resolver = new TemplateResolver(
            _services.GetRequiredService<IServiceScopeFactory>(),
            channels,
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(Now),
            Registry
        );
    }

    public IPlatformDirectory Directory { get; }

    public IKickApiClient Kick { get; }

    public IKickAccessTokenProvider KickTokens { get; }

    public YouTubeLiveChatSessionRegistry YouTubeSessions { get; }

    public IYouTubeLiveChatClient YouTube { get; }

    public ITwitchChannelsApi Twitch { get; }

    public IChannelAnalyticsService Analytics { get; }

    public ITemplateHelperRegistry Registry { get; }

    /// <summary>The seeds of a chat command typed by a Twitch viewer; the event came from Twitch too.</summary>
    public static Dictionary<string, string> Seeds() =>
        new()
        {
            ["user"] = "ViewerOne",
            ["user.name"] = "viewerone",
            ["user.id"] = "1001",
            ["user.provider"] = "twitch",
            ["provider"] = "twitch",
        };

    public Task<string> RenderAsync(string template, Dictionary<string, string>? seeds = null) =>
        _resolver.ResolveAsync(template, seeds ?? Seeds(), ChannelA);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    public static List<PlatformLink> AllThree() =>
        [
            new("twitch", ChannelA, "123456", "samplechannel", true),
            new("kick", KickChannel, "5555", "samplechannel", true),
            new("youtube", YouTubeChannel, "UCsample", "samplechannel", true),
        ];

    private static ChannelContext Context(bool streamIsOn) =>
        new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "123456",
            ChannelName = "samplechannel",
            DisplayName = "SampleChannel",
            IsLive = streamIsOn,
            ViewerCount = streamIsOn ? 42 : 0,
            CurrentTitle = streamIsOn ? "Building a bot" : null,
            WentLiveAt = streamIsOn ? LiveSince : null,
            Timezone = "Europe/Amsterdam",
            Language = "en",
        };

    private static List<ChannelAnalyticsPlatformSummaryDto> Summaries() =>
        [
            new("twitch", 300, 20, 4, 2, 100),
            new("kick", 120, 9, 1, 0, 0),
            new("youtube", 60, 6, 2, 0, 0),
        ];
}

/// <summary>What differs from the catalog fixture in one test.</summary>
internal sealed record PlatformWorld(
    List<PlatformLink>? Links = null,
    bool YouTubeIsLive = true,
    bool KickReadFails = false,
    bool StreamIsOn = true,
    List<ChannelAnalyticsPlatformSummaryDto>? Summaries = null
);
