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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Broadcast;

/// <summary>
/// The ads, schedule and chat-activity fixture of catalog section 5: the clock is 2026-10-26T14:30Z (Monday,
/// 15:30 in Europe/Amsterdam); the next ad is in 12m (90s long), the last one was 48m ago, two snoozes are left;
/// the schedule holds Friday 20:00 Bot night, Sunday 14:00 Chill and Tuesday 20:00 Bot night. Fakes sit at the
/// service interfaces; the resolver is the real one.
/// </summary>
internal sealed partial class BroadcastHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    public static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public BroadcastHelperFixture(
        string language = "en",
        bool vacation = false,
        bool segmentToday = false,
        bool noAd = false,
        bool failing = false
    )
    {
        Clock = new FakeTimeProvider(Now);
        Channels = Substitute.For<IChannelRegistry>();
        Channels.Get(ChannelA).Returns(Context(language));

        Ads = Substitute.For<ITwitchAdsApi>();
        Ads.GetAdScheduleAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(
                failing
                    ? Result.Failure<TwitchAdSchedule>("down", "twitch_error")
                    : Result.Success(AdSchedule(noAd))
            );

        Schedule = Substitute.For<ITwitchScheduleApi>();
        Schedule
            .GetScheduleAsync(ChannelA, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                failing
                    ? Result.Failure<TwitchSchedule>("down", "twitch_error")
                    : Result.Success(ScheduleOf(vacation, segmentToday))
            );

        Stats = Substitute.For<IStreamStatsService>();
        Analytics = Substitute.For<IChannelAnalyticsService>();
        ChatAssets = Substitute.For<ITwitchChatAssetsApi>();
        ConfigureActivity();

        ServiceCollection services = [];
        services
            .AddSingleton(Ads)
            .AddSingleton(Schedule)
            .AddSingleton(Stats)
            .AddSingleton(Analytics)
            .AddSingleton(ChatAssets)
            .AddSingleton<TimeProvider>(Clock)
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly);
        UseSeededActivityProvider(services);
        _services = services.BuildServiceProvider();
        FeedActivity();
        Registry = _services.GetRequiredService<ITemplateHelperRegistry>();
        _resolver = new TemplateResolver(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Channels,
            NullLogger<TemplateResolver>.Instance,
            Clock,
            Registry
        );
    }

    public FakeTimeProvider Clock { get; }

    public IChannelRegistry Channels { get; }

    public ITwitchAdsApi Ads { get; }

    public ITwitchScheduleApi Schedule { get; }

    public IStreamStatsService Stats { get; }

    public IChannelAnalyticsService Analytics { get; }

    public ITwitchChatAssetsApi ChatAssets { get; }

    public ITemplateHelperRegistry Registry { get; }

    public IServiceProvider Services => _services;

    public static Dictionary<string, string> Seeds() =>
        new()
        {
            ["user"] = "ViewerOne",
            ["user.name"] = "viewerone",
            ["user.id"] = "1001",
            ["user.provider"] = "twitch",
        };

    public Task<string> RenderAsync(string template, Dictionary<string, string>? seeds = null) =>
        _resolver.ResolveAsync(template, seeds ?? Seeds(), ChannelA);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    partial void ConfigureActivity();

    partial void UseSeededActivityProvider(ServiceCollection services);

    partial void FeedActivity();

    private static ChannelContext Context(string language)
    {
        ChannelContext context = new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "123456",
            ChannelName = "samplechannel",
            DisplayName = "SampleChannel",
            IsLive = true,
            WentLiveAt = new DateTimeOffset(2026, 10, 26, 12, 15, 0, TimeSpan.Zero),
            Timezone = "Europe/Amsterdam",
            Language = language,
            CommandsUsed = 88,
            LastActivityAt = Now.AddSeconds(-4),
        };
        for (int i = 1; i <= 33; i++)
            context.SessionChatters["id" + i] = "Chatter" + i;
        return context;
    }

    private static TwitchAdSchedule AdSchedule(bool none) =>
        none
            ? new TwitchAdSchedule(0, 0, 0, 0, 0, 0)
            : new TwitchAdSchedule(
                SnoozeCount: 2,
                SnoozeRefreshAt: Unix(Now.AddMinutes(34)),
                NextAdAt: Unix(Now.AddMinutes(12)),
                Duration: 90,
                LastAdAt: Unix(Now.AddMinutes(-48)),
                PrerollFreeTime: 1200
            );

    private static int Unix(DateTimeOffset moment) => (int)moment.ToUnixTimeSeconds();

    private static TwitchSchedule ScheduleOf(bool vacation, bool segmentToday)
    {
        List<TwitchScheduleSegment> segments =
        [
            Segment(
                "s2",
                Utc(2026, 10, 30, 19),
                3,
                "Bot night",
                "Software and Game Development",
                true
            ),
            Segment("s3", Utc(2026, 11, 1, 13), 2, "Chill", "Just Chatting", false),
            Segment(
                "s4",
                Utc(2026, 11, 3, 19),
                3,
                "Bot night",
                "Software and Game Development",
                true
            ),
        ];
        if (segmentToday)
            segments.Insert(0, Segment("s1", Utc(2026, 10, 26, 20), 1, "Quick one", null, false));

        segments.Add(
            Segment("s5", Utc(2026, 10, 28, 19), 3, "Canceled show", "Just Chatting", false) with
            {
                CanceledUntil = Utc(2026, 10, 29, 19),
            }
        );
        return new TwitchSchedule(
            segments,
            "123456",
            "SampleChannel",
            "samplechannel",
            vacation ? new TwitchScheduleVacation(Utc(2026, 11, 6, 0), Utc(2026, 11, 15, 22)) : null
        );
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private static TwitchScheduleSegment Segment(
        string id,
        DateTimeOffset start,
        int hours,
        string title,
        string? category,
        bool recurring
    ) =>
        new(
            id,
            start,
            start.AddHours(hours),
            title,
            null,
            category is null ? null : new TwitchScheduleCategory("1", category),
            recurring
        );
}
