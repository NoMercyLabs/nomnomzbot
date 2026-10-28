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

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Lookups;

/// <summary>
/// The lookup fixture of catalog section 5: clock 2026-10-26T14:30Z, channel zone Europe/Amsterdam, channel
/// <c>samplechannel</c> live since 12:15Z, ViewerTwo (login <c>viewertwo</c>) offline and playing Minecraft.
/// Twitch is a set of fakes at the Helix interfaces; every outside source is <see cref="StubLookupHandler"/>.
/// The resolver is the real one.
/// </summary>
internal sealed class LookupHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LiveSince = new(2026, 10, 26, 12, 15, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public LookupHelperFixture(
        string language = "en",
        bool openMeteoAllowed = true,
        bool hang = false,
        bool twitchDown = false
    )
    {
        Handler = new StubLookupHandler(hang);
        Users = Substitute.For<ITwitchUsersApi>();
        Streams = Substitute.For<ITwitchStreamsApi>();
        Channels = Substitute.For<ITwitchChannelsApi>();
        Games = Substitute.For<ITwitchGamesApi>();
        if (twitchDown)
            TwitchFailsEverywhere();
        else
            TwitchAnswers();

        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(ChannelA).Returns(Context(language));

        _services = new ServiceCollection()
            .AddSingleton(Users)
            .AddSingleton(Streams)
            .AddSingleton(Channels)
            .AddSingleton(Games)
            .BuildServiceProvider();

        TimeSpan shortLimit = TimeSpan.FromMilliseconds(150);
        LookupHttp http = new(() => new HttpClient(Handler, disposeHandler: false));
        LookupCache cache = new(new MemoryCache(new MemoryCacheOptions()));
        OpenMeteoLocationClient location = new(http, cache, hang ? shortLimit : null);
        LookupSources sources = new(
            location,
            new OpenMeteoWeatherClient(location, http, cache, hang ? shortLimit : null),
            new FrankfurterCurrencyClient(http, cache, hang ? shortLimit : null),
            new MyMemoryTranslationClient(http, cache, hang ? shortLimit : null),
            cache,
            openMeteoAllowed
        );
        Provider = new LookupTemplateHelpers(
            _services.GetRequiredService<IServiceScopeFactory>(),
            sources
        );
        Registry = new TemplateHelperRegistryAggregate([Provider, new IfTemplateHelpers()]);
        _resolver = new TemplateResolver(
            _services.GetRequiredService<IServiceScopeFactory>(),
            channels,
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(Now),
            Registry
        );
    }

    public StubLookupHandler Handler { get; }

    public ITwitchUsersApi Users { get; }

    public ITwitchStreamsApi Streams { get; }

    public ITwitchChannelsApi Channels { get; }

    public ITwitchGamesApi Games { get; }

    public ITemplateHelperProvider Provider { get; }

    public ITemplateHelperRegistry Registry { get; }

    public Task<string> RenderAsync(string template, Dictionary<string, string>? seeds = null) =>
        _resolver.ResolveAsync(template, seeds ?? [], ChannelA);

    public ValueTask DisposeAsync()
    {
        Handler.Dispose();
        return _services.DisposeAsync();
    }

    private void TwitchAnswers()
    {
        Users
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string login = call.Arg<IReadOnlyList<string>>()[0];
                List<TwitchUser> found = login switch
                {
                    "viewertwo" =>
                    [
                        User(
                            "1002",
                            "viewertwo",
                            "ViewerTwo",
                            new DateTimeOffset(2019, 5, 2, 9, 0, 0, TimeSpan.Zero)
                        ),
                    ],
                    "samplechannel" =>
                    [
                        User(
                            "123456",
                            "samplechannel",
                            "SampleChannel",
                            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)
                        ),
                    ],
                    _ => [],
                };
                return Result.Success<IReadOnlyList<TwitchUser>>(found);
            });

        Streams
            .GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                bool live = call.Arg<TwitchStreamsFilter>().UserLogins!.Contains("samplechannel");
                List<TwitchStream> items = live ? [SampleStream()] : [];
                return Result.Success(new TwitchPage<TwitchStream>(items, null, items.Count));
            });

        Channels
            .GetChannelInformationByTwitchIdsAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string id = call.Arg<IReadOnlyList<string>>()[0];
                List<TwitchChannelInformation> info =
                    id == "1002"
                        ? [Information(id, "viewertwo", "Minecraft", "Building castles")]
                        :
                        [
                            Information(
                                id,
                                "samplechannel",
                                "Software and Game Development",
                                "Building a bot"
                            ),
                        ];
                return Result.Success<IReadOnlyList<TwitchChannelInformation>>(info);
            });
        Channels
            .GetChannelFollowerCountAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(Result.Success(1234));
        Channels
            .GetChannelFollowerAsync(ChannelA, "1002", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<TwitchChannelFollower?>(
                    new TwitchChannelFollower(
                        "1002",
                        "viewertwo",
                        "ViewerTwo",
                        new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero)
                    )
                )
            );

        Games
            .GetGamesAsync(
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Is<IReadOnlyList<string>?>(n => n != null && n[0] == "Minecraft"),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchGame>>([
                    new TwitchGame(
                        "27471",
                        "Minecraft",
                        "https://static-cdn.jtvnw.net/ttv-boxart/27471-{width}x{height}.jpg",
                        "7"
                    ),
                ])
            );
        Games
            .GetTopGamesAsync(Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchGame>(
                        [
                            new TwitchGame(
                                "509658",
                                "Just Chatting",
                                "https://static-cdn.jtvnw.net/ttv-boxart/509658-{width}x{height}.jpg",
                                "1"
                            ),
                        ],
                        null,
                        1
                    )
                )
            );
    }

    private void TwitchFailsEverywhere()
    {
        Users
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<TwitchUser>>("TWITCH_DOWN", "Twitch is down"));
        Streams
            .GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchPage<TwitchStream>>("TWITCH_DOWN", "Twitch is down"));
    }

    private static TwitchUser User(
        string id,
        string login,
        string display,
        DateTimeOffset createdAt
    ) =>
        new(
            id,
            login,
            display,
            "",
            "",
            "",
            $"https://static-cdn.jtvnw.net/jtv_user_pictures/{login}.png",
            "",
            0,
            createdAt
        );

    private static TwitchChannelInformation Information(
        string id,
        string login,
        string game,
        string title
    ) => new(id, login, login, "en", "1", game, title, 0, [], [], false);

    private static TwitchStream SampleStream() =>
        new(
            "1",
            "123456",
            "samplechannel",
            "SampleChannel",
            "1",
            "Software and Game Development",
            "live",
            "Building a bot",
            [],
            42,
            LiveSince,
            "en",
            "",
            false
        );

    private static ChannelContext Context(string language) =>
        new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "123456",
            ChannelName = "samplechannel",
            DisplayName = "SampleChannel",
            IsLive = true,
            WentLiveAt = LiveSince,
            Timezone = "Europe/Amsterdam",
            Language = language,
        };
}
