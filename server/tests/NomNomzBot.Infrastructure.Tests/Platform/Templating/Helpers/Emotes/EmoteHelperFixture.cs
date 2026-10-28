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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Emotes;

/// <summary>
/// The emote fixture of catalog section 7.13 on the clock and channel of section 5. The channel has four Twitch
/// emotes (two subscriber, one follower, one Bits tier); its 7TV set holds catJAM, KEKW and PepeLaugh, BTTV
/// peepoHappy and widepeepo, FFZ monkaS; Twitch globals are Kappa and LUL. Fakes sit at the service
/// interfaces; the resolver is the real one and its random is seeded.
/// </summary>
internal sealed class EmoteHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid OwnerId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    public const string CurrentStream = "stream-1";
    public const string ViewerOneTwitchId = "1001";

    private static readonly Guid ViewerOneId = Guid.Parse("00000000-0000-0000-0000-000000001001");
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public EmoteHelperFixture(bool assetsFail = false, int seed = 1)
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(ChannelA).Returns(Context());

        Assets = Substitute.For<ITwitchChatAssetsApi>();
        Assets
            .GetChannelEmotesAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(
                assetsFail
                    ? Result.Failure<IReadOnlyList<TwitchChannelEmote>>("down", "twitch_down")
                    : Result.Success<IReadOnlyList<TwitchChannelEmote>>(ChannelEmotes())
            );
        Assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(
                assetsFail
                    ? Result.Failure<IReadOnlyList<TwitchGlobalEmote>>("down", "twitch_down")
                    : Result.Success<IReadOnlyList<TwitchGlobalEmote>>(GlobalEmotes())
            );

        Catalogue = Substitute.For<IChatEmoteCatalogue>();
        Catalogue
            .GetForChannelAsync(
                ChannelA,
                Guid.Empty,
                ChatEmoteSender.Bot,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<ChatEmote>>(ThirdPartyEmotes()));

        IFavoriteEmoteService favorites = Substitute.For<IFavoriteEmoteService>();
        favorites
            .ListAsync(OwnerId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<FavoriteEmoteDto>>(Favorites()));

        ITwitchBitsApi bits = Substitute.For<ITwitchBitsApi>();
        bits.GetCheermotesAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchCheermote>>(Cheermotes()));

        Random = new Random(seed);
        _services = new ServiceCollection()
            .AddSingleton(Assets)
            .AddSingleton(Catalogue)
            .AddSingleton(favorites)
            .AddSingleton(bits)
            .AddSingleton(Random)
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

    public ITwitchChatAssetsApi Assets { get; }

    public IChatEmoteCatalogue Catalogue { get; }

    public Random Random { get; }

    public ITemplateHelperRegistry Registry { get; }

    /// <summary>A chat command typed by ViewerOne whose message holds three emotes (KEKW twice, catJAM once).</summary>
    public static Dictionary<string, string> Seeds() =>
        new()
        {
            ["user"] = "ViewerOne",
            ["user.name"] = "viewerone",
            ["user.id"] = ViewerOneTwitchId,
            ["user.provider"] = "twitch",
            ["message"] = "KEKW look at catJAM and KEKW",
        };

    public Task<string> RenderAsync(string template, Dictionary<string, string>? seeds = null) =>
        _resolver.ResolveAsync(template, seeds ?? Seeds(), ChannelA);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static ChannelContext Context() =>
        new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "123456",
            ChannelName = "samplechannel",
            DisplayName = "SampleChannel",
            IsLive = true,
            CurrentStreamId = CurrentStream,
            WentLiveAt = new DateTimeOffset(2026, 10, 26, 12, 15, 0, TimeSpan.Zero),
            Timezone = "Europe/Amsterdam",
            Language = "en",
        };

    private static TwitchEmoteImages Pictures(string id) =>
        new(
            $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/static/dark/1.0",
            $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/static/dark/2.0",
            $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/static/dark/3.0"
        );

    private static TwitchChannelEmote Channel(string id, string name, string type) =>
        new(id, name, Pictures(id), "1000", type, "set-1", ["static"], ["1.0"], ["dark"]);

    private static List<TwitchChannelEmote> ChannelEmotes() =>
        [
            Channel("c1", "sampleHype", EmoteItem.SubscriberType),
            Channel("c2", "sampleLove", EmoteItem.SubscriberType),
            Channel("c3", "sampleWave", EmoteItem.FollowerType),
            Channel("c4", "sampleBits", EmoteItem.BitsType),
        ];

    private static List<TwitchGlobalEmote> GlobalEmotes() =>
        [
            new("25", "Kappa", Pictures("25"), ["static"], ["1.0"], ["dark"]),
            new("425618", "LUL", Pictures("425618"), ["static"], ["1.0"], ["dark"]),
        ];

    private static ChatEmote Third(
        EmoteProvider provider,
        string code,
        ChatEmoteOrigin origin,
        string host
    ) =>
        new(
            provider,
            code.ToLowerInvariant(),
            code,
            new Dictionary<string, string>
            {
                ["1"] = $"https://{host}/{code}/1x.webp",
                ["2"] = $"https://{host}/{code}/2x.webp",
            },
            Animated: true,
            ZeroWidth: false,
            Origin: origin
        );

    private static List<ChatEmote> ThirdPartyEmotes() =>
        [
            Third(EmoteProvider.Twitch, "Kappa", ChatEmoteOrigin.Global, "twitch.example"),
            Third(EmoteProvider.SevenTv, "catJAM", ChatEmoteOrigin.Channel, "cdn.7tv.app"),
            Third(EmoteProvider.SevenTv, "KEKW", ChatEmoteOrigin.Channel, "cdn.7tv.app"),
            Third(EmoteProvider.SevenTv, "PepeLaugh", ChatEmoteOrigin.Channel, "cdn.7tv.app"),
            Third(EmoteProvider.SevenTv, "Clap", ChatEmoteOrigin.Global, "cdn.7tv.app"),
            Third(EmoteProvider.Bttv, "peepoHappy", ChatEmoteOrigin.Channel, "cdn.betterttv.net"),
            Third(EmoteProvider.Bttv, "widepeepo", ChatEmoteOrigin.Channel, "cdn.betterttv.net"),
            Third(EmoteProvider.Ffz, "monkaS", ChatEmoteOrigin.Channel, "cdn.frankerfacez.com"),
            Third(EmoteProvider.Ffz, "ZreknarF", ChatEmoteOrigin.Global, "cdn.frankerfacez.com"),
        ];

    private static List<FavoriteEmoteDto> Favorites() =>
        [
            new(
                "7tv",
                "catjam",
                "catJAM",
                null,
                new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)
            ),
            new("7tv", "kekw", "KEKW", null, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
        ];

    private static TwitchCheermoteImageScales Scales() => new(new Dictionary<string, string>());

    private static TwitchCheermoteImageFormats Formats() => new(Scales(), Scales());

    private static TwitchCheermoteTier Tier(int minBits, bool canCheer = true) =>
        new(
            minBits,
            minBits.ToString(),
            "#fff",
            new TwitchCheermoteImages(Formats(), Formats()),
            canCheer,
            true
        );

    private static List<TwitchCheermote> Cheermotes() =>
        [
            new("Party", [Tier(1), Tier(100)], "global_third_party", 2, Now, false),
            new(
                "Cheer",
                [Tier(1), Tier(100), Tier(1000), Tier(5000, canCheer: false)],
                "global_first_party",
                1,
                Now,
                false
            ),
        ];

    /// <summary>
    /// Newest first. ViewerOne (1001): KEKW 4, catJAM 1. ViewerTwo (1002): catJAM 4. ViewerThree (1003): KEKW 5.
    /// This stream (stream-1): KEKW 2, catJAM 5. Words that only look like an emote (KEKWait) do not count.
    /// </summary>
    internal static List<EmoteChatLine> Lines() =>
        [
            new("1001", "KEKW catJAM KEKW", CurrentStream),
            new("1002", "catJAM catJAM catJAM catJAM", CurrentStream),
            new("1002", "KEKWait what", CurrentStream),
            new("1001", "KEKW", "stream-0"),
            new("1001", "hello KEKW Kappa", "stream-0"),
            new("1003", "KEKW KEKW KEKW KEKW KEKW", "stream-0"),
        ];

    private sealed class FakeChat : IEmoteChatSource
    {
        public Task<IReadOnlyList<EmoteChatLine>> RecentLinesAsync(
            IServiceProvider services,
            Guid broadcasterId,
            int take,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<EmoteChatLine>>(Lines());

        public Task<Guid?> OwnerUserIdAsync(
            IServiceProvider services,
            Guid broadcasterId,
            CancellationToken cancellationToken
        ) => Task.FromResult<Guid?>(OwnerId);
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
                    ? new ViewerRef(
                        ViewerOneId,
                        ViewerOneTwitchId,
                        "viewerone",
                        "ViewerOne",
                        "twitch"
                    )
                    : null
            );
    }
}
