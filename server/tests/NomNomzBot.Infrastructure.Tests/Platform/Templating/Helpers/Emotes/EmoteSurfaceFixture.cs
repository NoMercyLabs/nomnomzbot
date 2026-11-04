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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Emotes;

/// <summary>The emote namespace's channel, global, third-party and chat-history answers.</summary>
internal static class EmoteSurfaceFixture
{
    private static readonly Guid OwnerId = Guid.Parse("0192b400-0000-7000-8000-00000000f047");

    public static void Configure(ServiceCollection services, Guid channel)
    {
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetChannelEmotesAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEmote>>([
                    ChannelEmote("sampleHype", "subscriptions"),
                    ChannelEmote("sampleWave", "follower"),
                    ChannelEmote("sampleBits", "bitstier"),
                ])
            );
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchGlobalEmote>>([
                    new TwitchGlobalEmote(
                        "25",
                        "Kappa",
                        Images("25"),
                        ["static"],
                        ["1.0"],
                        ["dark"]
                    ),
                ])
            );

        IChatEmoteCatalogue catalogue = Substitute.For<IChatEmoteCatalogue>();
        catalogue
            .GetForChannelAsync(
                channel,
                Guid.Empty,
                ChatEmoteSender.Bot,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<ChatEmote>>([
                    ThirdParty(EmoteProvider.SevenTv, "catJAM", "cdn.7tv.app"),
                    ThirdParty(EmoteProvider.Bttv, "peepoHappy", "cdn.betterttv.net"),
                    ThirdParty(EmoteProvider.Ffz, "monkaS", "cdn.frankerfacez.com"),
                ])
            );

        IFavoriteEmoteService favorites = Substitute.For<IFavoriteEmoteService>();
        favorites
            .ListAsync(OwnerId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<FavoriteEmoteDto>>([
                    new FavoriteEmoteDto(
                        "7tv",
                        "catjam",
                        "catJAM",
                        null,
                        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
                    ),
                ])
            );

        ITwitchBitsApi bits = Substitute.For<ITwitchBitsApi>();
        bits.GetCheermotesAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchCheermote>>([
                    new TwitchCheermote(
                        "Cheer",
                        [
                            new TwitchCheermoteTier(
                                1,
                                "1",
                                "#fff",
                                new TwitchCheermoteImages(Formats(), Formats()),
                                true,
                                true
                            ),
                        ],
                        "global_first_party",
                        1,
                        new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.Zero),
                        false
                    ),
                ])
            );

        services.AddSingleton(assets);
        services.AddSingleton(catalogue);
        services.AddSingleton(favorites);
        services.AddSingleton(bits);
        services.AddSingleton<IEmoteChatSource>(new MatrixEmoteChatSource());
    }

    private static TwitchEmoteImages Images(string id) =>
        new(
            $"https://static.example/{id}/1x.png",
            $"https://static.example/{id}/2x.png",
            $"https://static.example/{id}/3x.png"
        );

    private static TwitchChannelEmote ChannelEmote(string code, string type) =>
        new(code, code, Images(code), "1000", type, "set-1", ["static"], ["1.0"], ["dark"]);

    private static ChatEmote ThirdParty(EmoteProvider provider, string code, string host) =>
        new(
            provider,
            code.ToLowerInvariant(),
            code,
            new Dictionary<string, string> { ["1"] = $"https://{host}/{code}/1x.webp" },
            Animated: true,
            ZeroWidth: false,
            Origin: ChatEmoteOrigin.Channel
        );

    private static TwitchCheermoteImageFormats Formats() =>
        new(
            new TwitchCheermoteImageScales(new Dictionary<string, string>()),
            new TwitchCheermoteImageScales(new Dictionary<string, string>())
        );

    private sealed class MatrixEmoteChatSource : IEmoteChatSource
    {
        public Task<IReadOnlyList<EmoteChatLine>> RecentLinesAsync(
            IServiceProvider services,
            Guid broadcasterId,
            int take,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<EmoteChatLine>>([
                new("100000042", "catJAM catJAM", "stream-1"),
                new("100000043", "peepoHappy", "stream-1"),
            ]);

        public Task<Guid?> OwnerUserIdAsync(
            IServiceProvider services,
            Guid broadcasterId,
            CancellationToken cancellationToken
        ) => Task.FromResult<Guid?>(OwnerId);
    }
}
