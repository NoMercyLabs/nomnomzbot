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
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Community.Services;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.LiveEvents;

/// <summary>
/// Three channels at the catalog section 5 clock (14:30:00Z). A: poll chips 12 / cookies 8 with 90 s left and
/// a prediction Yes 3,000 / No 1,000 open for 45 s more. B: nothing running. C: a tied poll and a locked prediction.
/// </summary>
internal sealed class PollPredictionHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid ChannelB = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    public static readonly Guid ChannelC = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public PollPredictionHelperFixture()
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(ChannelA).Returns(ContextOf(ChannelA, "samplechannel", "en"));
        channels.Get(ChannelB).Returns(ContextOf(ChannelB, "otherchannel", "en"));
        channels.Get(ChannelC).Returns(ContextOf(ChannelC, "thirdchannel", "nl"));

        _services = new ServiceCollection()
            .AddSingleton(PollsApi())
            .AddSingleton(PredictionsApi())
            .AddSingleton(History())
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

    public ITemplateHelperRegistry Registry { get; }

    /// <summary>How many times the polls API, the predictions API and the history service were asked.</summary>
    public int PollCalls { get; private set; }

    public int PredictionCalls { get; private set; }

    public int HistoryCalls { get; private set; }

    public Task<string> RenderAsync(string template, Guid? broadcasterId) =>
        _resolver.ResolveAsync(template, new Dictionary<string, string>(), broadcasterId);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static ChannelContext ContextOf(Guid id, string login, string language) =>
        new()
        {
            BroadcasterId = id,
            TwitchChannelId = "123456",
            ChannelName = login,
            DisplayName = login,
            IsLive = true,
            Timezone = "Europe/Amsterdam",
            Language = language,
        };

    private ITwitchPollsApi PollsApi()
    {
        ITwitchPollsApi api = Substitute.For<ITwitchPollsApi>();
        api.GetPollsAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                PollCalls++;
                Guid id = call.ArgAt<Guid>(0);
                List<TwitchPoll> polls =
                    id == ChannelA ? [PollOf(12, 8)]
                    : id == ChannelC ? [PollOf(5, 5)]
                    : [];
                return Task.FromResult(
                    Result.Success(new TwitchPage<TwitchPoll>(polls, null, polls.Count))
                );
            });
        return api;
    }

    private static TwitchPoll PollOf(int chips, int cookies) =>
        new(
            "poll-1",
            "123456",
            "SampleChannel",
            "samplechannel",
            "Best snack?",
            [
                new TwitchPollChoice("c1", "chips", chips, 0, 0),
                new TwitchPollChoice("c2", "cookies", cookies, 0, 0),
            ],
            false,
            0,
            true,
            100,
            "ACTIVE",
            120,
            Now.AddSeconds(-30),
            null
        );

    private ITwitchPredictionsApi PredictionsApi()
    {
        ITwitchPredictionsApi api = Substitute.For<ITwitchPredictionsApi>();
        api.GetPredictionsAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                PredictionCalls++;
                Guid id = call.ArgAt<Guid>(0);
                List<TwitchPrediction> predictions =
                    id == ChannelA ? [PredictionOf("ACTIVE")]
                    : id == ChannelC ? [PredictionOf("LOCKED")]
                    : [];
                return Task.FromResult(
                    Result.Success(
                        new TwitchPage<TwitchPrediction>(predictions, null, predictions.Count)
                    )
                );
            });
        return api;
    }

    private static TwitchPrediction PredictionOf(string status) =>
        new(
            "pred-1",
            "123456",
            "SampleChannel",
            "samplechannel",
            "Win?",
            null,
            [
                new TwitchPredictionOutcome(
                    "o1",
                    "Yes",
                    9,
                    3000,
                    [new TwitchPredictionTopPredictor("1001", "ViewerOne", "viewerone", 1000, 0)],
                    "BLUE"
                ),
                new TwitchPredictionOutcome(
                    "o2",
                    "No",
                    3,
                    1000,
                    [new TwitchPredictionTopPredictor("1002", "ViewerTwo", "viewertwo", 400, 0)],
                    "PINK"
                ),
            ],
            60,
            status,
            Now.AddSeconds(-15),
            null,
            null
        );

    private IPollPredictionHistoryService History()
    {
        IPollPredictionHistoryService service = Substitute.For<IPollPredictionHistoryService>();
        service
            .ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                HistoryCalls++;
                IReadOnlyList<PollPredictionHistoryItemDto> items =
                    call.ArgAt<Guid>(0) == ChannelA ? HistoryOfA() : [];
                return Task.FromResult(Result.Success(items));
            });
        return service;
    }

    /// <summary>Newest first: the running poll and a newer chat poll must never count as the last poll or prediction.</summary>
    private static IReadOnlyList<PollPredictionHistoryItemDto> HistoryOfA() =>
        [
            Item("chat-1", "chat-poll", "Next game?", "closed", 0, "Minecraft", "Tetris"),
            Item("poll-1", "poll", "Best snack?", "unfinished", 1, "chips", "cookies"),
            Item("pred-0", "prediction", "Win?", "RESOLVED", 3, "Yes", "No"),
            Item("poll-0", "poll", "Best snack?", "COMPLETED", 5, "chips", "cookies"),
            Item("poll-x", "poll", "Older poll?", "COMPLETED", 9, "a", "b"),
        ];

    private static PollPredictionHistoryItemDto Item(
        string id,
        string kind,
        string title,
        string status,
        int hoursAgo,
        string first,
        string second
    )
    {
        bool ended = status != "unfinished";
        return new PollPredictionHistoryItemDto(
            id,
            kind,
            title,
            status,
            Now.AddHours(-hoursAgo),
            ended ? Now.AddHours(-hoursAgo).AddMinutes(2) : null,
            20,
            [
                new PollPredictionOptionDto("1", first, 12, 0, 60, ended),
                new PollPredictionOptionDto("2", second, 8, 0, 40, false),
            ]
        );
    }
}
