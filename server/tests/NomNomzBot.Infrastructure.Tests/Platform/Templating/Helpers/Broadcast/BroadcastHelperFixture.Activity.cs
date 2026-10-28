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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Broadcast;

/// <summary>
/// The chat-activity part of the fixture (catalog section 5): 18 messages in the last 60 s (ViewerTwo last),
/// 62 more in 60..300 s and 1000 more in 300..3590 s, mostly ViewerTwo; exactly 12 distinct authors in 10 minutes
/// (ViewerOne, ViewerTwo, Fan1..Fan10). The ring reads its own clock so history can be laid down in order.
/// </summary>
internal sealed partial class BroadcastHelperFixture
{
    public const string ViewerOneId = "1001";
    public const string ViewerTwoId = "1002";

    private readonly FakeTimeProvider _ringClock = new(Now.AddSeconds(-3590));

    public ChatActivityRing Ring { get; private set; } = null!;

    /// <summary>Moves the resolver's clock and the ring's clock together.</summary>
    public void Advance(TimeSpan span)
    {
        Clock.Advance(span);
        _ringClock.Advance(span);
    }

    partial void ConfigureActivity()
    {
        Stats
            .GetAsync(
                ChannelA,
                null,
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(StreamStats()));
        Analytics
            .GetDailySeriesAsync(ChannelA, Today, Today, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ChannelAnalyticsDailyDto>>([Day(2610)]));
        Analytics
            .GetTopViewersAsync(
                ChannelA,
                TopViewerMetric.Messages,
                Today,
                Today,
                1,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TopViewerDto>>([new(Guid.NewGuid(), "ViewerOne", 140)])
            );
        Analytics
            .GetChatHoursAsync(ChannelA, Today.AddDays(-90), Today, Arg.Any<CancellationToken>())
            .Returns(Result.Success(ChatHours()));
        ChatAssets
            .GetChattersAsync(ChannelA, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TwitchPage<TwitchChatter>([], null, 51)));
    }

    partial void UseSeededActivityProvider(ServiceCollection services)
    {
        Ring = new ChatActivityRing(_ringClock);
        foreach (
            ServiceDescriptor descriptor in services
                .Where(d => d.ImplementationType == typeof(ChatActivityTemplateHelpers))
                .ToList()
        )
            services.Remove(descriptor);

        services.AddSingleton<ITemplateHelperProvider>(sp => new ChatActivityTemplateHelpers(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Ring,
            new Random(1)
        ));
    }

    partial void FeedActivity()
    {
        for (int i = 0; i < 1000; i++)
        {
            SetRingClock(3590 - i * 3290 / 1000);
            if (i >= 990)
                Say("Fan" + (i - 989), "200" + (i - 989));
            else if (i % 10 == 0)
                Say("ViewerOne", ViewerOneId);
            else
                Say("ViewerTwo", ViewerTwoId);
        }

        for (int j = 0; j < 62; j++)
        {
            SetRingClock(299 - j * 3);
            SayOneOrTwo(j);
        }

        for (int k = 0; k < 18; k++)
        {
            SetRingClock(59 - k * 3);
            SayOneOrTwo(k);
        }

        SetRingClock(0);
    }

    private static DateOnly Today => new(2026, 10, 26);

    private void SetRingClock(int secondsBeforeNow)
    {
        TimeSpan wanted = Now.AddSeconds(-secondsBeforeNow) - _ringClock.GetUtcNow();
        if (wanted > TimeSpan.Zero)
            _ringClock.Advance(wanted);
    }

    private void SayOneOrTwo(int index)
    {
        if (index % 2 == 0)
            Say("ViewerOne", ViewerOneId);
        else
            Say("ViewerTwo", ViewerTwoId);
    }

    private void Say(string name, string userId) => Ring.Record(ChannelA, userId, name);

    private static StreamStatsDto StreamStats() =>
        new()
        {
            Stream = new StreamStatsWindowDto { Id = "40123456789" },
            Chat = new StreamStatsChatDto { Messages = 2430, UniqueChatters = 33 },
            TopChatters =
            [
                new StreamStatsNamedCountDto { Name = "ViewerOne", Count = 140 },
                new StreamStatsNamedCountDto { Name = "ViewerTwo", Count = 96 },
                new StreamStatsNamedCountDto { Name = "ViewerThree", Count = 40 },
            ],
        };

    private static ChannelAnalyticsDailyDto Day(long messages) =>
        new(Today, 33, messages, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null);

    private static ChannelChatHoursDto ChatHours() =>
        new(
            "Europe/Amsterdam",
            [
                new ChatHourCellDto(5, 21, 400, 20),
                new ChatHourCellDto(5, 20, 300, 18),
                new ChatHourCellDto(2, 21, 200, 15),
                new ChatHourCellDto(0, 14, 100, 9),
            ]
        );
}
