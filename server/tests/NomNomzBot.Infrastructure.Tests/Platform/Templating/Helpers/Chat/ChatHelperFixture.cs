// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Chat;

/// <summary>
/// The chat fixture of catalog section 7.14 on the clock and channel of section 5. The chat modes are slow 3s,
/// followers-only 10m, everything else off; "Welcome to the stream" is pinned; the pronunciation list turns
/// "gg" into "good game"; KEKW is an emote of the channel. With <c>shared</c> the channel is in a shared chat
/// session with OtherStreamer. Fakes sit at the service interfaces; the resolver is the real one.
/// </summary>
internal sealed class ChatHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public const string ChannelTwitchId = "123456";

    private static readonly Guid ViewerOneId = Guid.Parse("00000000-0000-0000-0000-000000001001");
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public ChatHelperFixture(
        bool shared = false,
        string language = "en",
        bool modeReadFails = false,
        bool pinned = true
    )
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(ChannelA).Returns(Context(language));

        ChatApi = Substitute.For<ITwitchChatApi>();
        ChatApi
            .GetChatSettingsAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(
                modeReadFails
                    ? Result.Failure<TwitchChatSettings>("down", "twitch_down")
                    : Result.Success(Settings())
            );
        ChatApi
            .GetPinnedMessagesAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(
                pinned
                    ? Result.Success(
                        new TwitchPinnedChatMessage(
                            "pin-1",
                            "Welcome to the stream",
                            Now,
                            null,
                            true,
                            false
                        )
                    )
                    : Result.Failure<TwitchPinnedChatMessage>("none", TwitchErrorCodes.NotFound)
            );

        Assets = Substitute.For<ITwitchChatAssetsApi>();
        Assets
            .GetChannelEmotesAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([]));
        Assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchGlobalEmote>>([]));
        Assets
            .GetSharedChatSessionAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(
                shared
                    ? Result.Success(
                        new TwitchSharedChatSession(
                            "session-1",
                            ChannelTwitchId,
                            [new(ChannelTwitchId), new("2002")],
                            Now,
                            Now
                        )
                    )
                    : Result.Failure<TwitchSharedChatSession>("none", TwitchErrorCodes.NotFound)
            );

        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "2002"),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    new("2002", "otherstreamer", "OtherStreamer", "", "", "", "", "", 0, Now),
                ])
            );

        Tracker = Substitute.For<ISharedChatSessionTracker>();
        Tracker
            .GetActiveSession(ChannelA)
            .Returns(
                shared
                    ? new SharedChatSessionInfo(
                        "session-1",
                        ChannelTwitchId,
                        [ChannelTwitchId, "2002"]
                    )
                    : null
            );

        Lexicon = Substitute.For<ITtsLexiconService>();
        Lexicon
            .ApplyAsync(ChannelA, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<string>(1).Replace("gg", "good game")));

        IChatEmoteCatalogue catalogue = Substitute.For<IChatEmoteCatalogue>();
        catalogue
            .GetForChannelAsync(
                ChannelA,
                Guid.Empty,
                ChatEmoteSender.Bot,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<ChatEmote>>([Emote("KEKW"), Emote("catJAM")]));

        _services = new ServiceCollection()
            .AddSingleton(ChatApi)
            .AddSingleton(Assets)
            .AddSingleton(users)
            .AddSingleton(Tracker)
            .AddSingleton(Lexicon)
            .AddSingleton(catalogue)
            .AddSingleton<IViewerDirectory, FakeDirectory>()
            .AddSingleton<IEmoteChatSource, FakeChat>()
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

    public ITwitchChatApi ChatApi { get; }

    public ITwitchChatAssetsApi Assets { get; }

    public ISharedChatSessionTracker Tracker { get; }

    public ITtsLexiconService Lexicon { get; }

    public ITemplateHelperRegistry Registry { get; }

    /// <summary>A chat command typed by ViewerOne on Twitch whose message is "hello big world".</summary>
    public static Dictionary<string, string> Seeds() =>
        new()
        {
            ["user"] = "ViewerOne",
            ["user.name"] = "viewerone",
            ["user.id"] = "1001",
            ["user.provider"] = "twitch",
            ["user.bits"] = "0",
            ["message"] = "hello big world",
            ["target"] = "ViewerTwo",
            ["channel.url"] = "https://twitch.tv/samplechannel",
        };

    public Task<string> RenderAsync(string template, Dictionary<string, string>? seeds = null) =>
        _resolver.ResolveAsync(template, seeds ?? Seeds(), ChannelA);

    public Task<string> RenderWithoutChannelAsync(string template) =>
        _resolver.ResolveAsync(template, Seeds(), null);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static ChannelContext Context(string language) =>
        new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = ChannelTwitchId,
            ChannelName = "samplechannel",
            DisplayName = "SampleChannel",
            IsLive = true,
            CurrentStreamId = "stream-1",
            WentLiveAt = new DateTimeOffset(2026, 10, 26, 12, 15, 0, TimeSpan.Zero),
            Timezone = "Europe/Amsterdam",
            Language = language,
        };

    private static TwitchChatSettings Settings() =>
        new(
            ChannelTwitchId,
            EmoteMode: false,
            FollowerMode: true,
            FollowerModeDuration: 10,
            ModeratorId: null,
            NonModeratorChatDelay: false,
            NonModeratorChatDelayDuration: null,
            SlowMode: true,
            SlowModeWaitTime: 3,
            SubscriberMode: false,
            UniqueChatMode: false
        );

    private static ChatEmote Emote(string code) =>
        new(
            EmoteProvider.SevenTv,
            code.ToLowerInvariant(),
            code,
            new Dictionary<string, string> { ["1"] = $"https://cdn.7tv.app/{code}/1x.webp" },
            Animated: true,
            ZeroWidth: false,
            Origin: ChatEmoteOrigin.Channel
        );

    private sealed class FakeChat : IEmoteChatSource
    {
        public Task<IReadOnlyList<EmoteChatLine>> RecentLinesAsync(
            IServiceProvider services,
            Guid broadcasterId,
            int take,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<EmoteChatLine>>([]);

        public Task<Guid?> OwnerUserIdAsync(
            IServiceProvider services,
            Guid broadcasterId,
            CancellationToken cancellationToken
        ) => Task.FromResult<Guid?>(null);
    }

    private sealed class FakeDirectory : IViewerDirectory
    {
        public Task<ViewerRef?> ResolveAsync(
            IServiceProvider services,
            ViewerSide side,
            IReadOnlyDictionary<string, string> seeds,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<ViewerRef?>(
                side == ViewerSide.User
                    ? new ViewerRef(ViewerOneId, "1001", "viewerone", "ViewerOne", "twitch")
                    : null
            );
    }
}
