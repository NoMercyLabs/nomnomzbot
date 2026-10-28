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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.LiveEvents;

/// <summary>
/// The hype train, goal and charity helpers against the fixed data of catalog section 5. Fakes sit at the three
/// Twitch service interfaces; the clock is a <see cref="FakeTimeProvider"/>.
/// </summary>
internal sealed class LiveEventsHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public LiveEventsHelperFixture(
        bool idle = false,
        bool withNewSubGoal = false,
        string trainType = "regular",
        bool sharedTrain = false
    )
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(ChannelA).Returns(Context());

        HypeTrain = Substitute.For<ITwitchHypeTrainApi>();
        HypeTrain
            .GetHypeTrainStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                idle
                    ? Result.Failure<TwitchHypeTrainStatus>("none", "not_found")
                    : Result.Success(TrainStatus(trainType, sharedTrain))
            );

        Goals = Substitute.For<ITwitchGoalsApi>();
        Goals
            .GetCreatorGoalsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                idle
                    ? Result.Failure<IReadOnlyList<TwitchCreatorGoal>>("none", "not_found")
                    : Result.Success<IReadOnlyList<TwitchCreatorGoal>>(GoalList(withNewSubGoal))
            );

        Charity = Substitute.For<ITwitchCharityApi>();
        Charity
            .GetCharityCampaignAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                idle
                    ? Result.Failure<TwitchCharityCampaign>("none", "not_found")
                    : Result.Success(Campaign())
            );
        Charity
            .GetCharityCampaignDonationsAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                idle
                    ? Result.Failure<TwitchPage<TwitchCharityDonation>>("none", "not_found")
                    : Result.Success(Donations())
            );

        _services = new ServiceCollection()
            .AddSingleton(HypeTrain)
            .AddSingleton(Goals)
            .AddSingleton(Charity)
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

    public ITwitchHypeTrainApi HypeTrain { get; }

    public ITwitchGoalsApi Goals { get; }

    public ITwitchCharityApi Charity { get; }

    public ITemplateHelperRegistry Registry { get; }

    public Task<string> RenderAsync(string template) =>
        _resolver.ResolveAsync(template, new Dictionary<string, string>(), ChannelA);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static ChannelContext Context() =>
        new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "123456",
            ChannelName = "samplechannel",
            DisplayName = "SampleChannel",
            IsLive = true,
            Timezone = "Europe/Amsterdam",
            Language = "en",
        };

    /// <summary>Level 3, 1,800 of 3,000 (60%), 5,400 in total, 4m 10s left, record level 5.</summary>
    private static TwitchHypeTrainStatus TrainStatus(string type, bool shared) =>
        new(
            new TwitchHypeTrain(
                "train1",
                "123456",
                "samplechannel",
                "SampleChannel",
                3,
                5400,
                1800,
                3000,
                [
                    new TwitchHypeTrainContribution("1002", "viewertwo", "ViewerTwo", "BITS", 300),
                    new TwitchHypeTrainContribution(
                        "1001",
                        "viewerone",
                        "ViewerOne",
                        "SUBSCRIPTION",
                        1000
                    ),
                ],
                null,
                Now.AddMinutes(-10),
                Now.AddMinutes(4).AddSeconds(10),
                type,
                shared
            ),
            new TwitchHypeTrainRecord(5, 9000, Now.AddDays(-30)),
            null
        );

    /// <summary>Followers 1,234 of 1,500 and subs 80 of 100; the optional new-subs goal is 6 of 20.</summary>
    private static List<TwitchCreatorGoal> GoalList(bool withNewSubGoal)
    {
        List<TwitchCreatorGoal> goals =
        [
            Goal("g1", "follower", "Road to 1.5k", 1234, 1500),
            Goal("g2", "subscription", "Sub goal", 80, 100),
        ];
        if (withNewSubGoal)
            goals.Add(Goal("g3", "new_subscription", "New subs today", 6, 20));
        return goals;
    }

    private static TwitchCreatorGoal Goal(
        string id,
        string type,
        string description,
        int current,
        int target
    ) =>
        new(
            id,
            "123456",
            "SampleChannel",
            "samplechannel",
            type,
            description,
            current,
            target,
            Now.AddDays(-2)
        );

    private static TwitchCharityCampaign Campaign() =>
        new(
            "camp1",
            "123456",
            "samplechannel",
            "SampleChannel",
            "Sample Charity",
            "A sample charity",
            "https://example.org/logo.png",
            "https://www.samplecharity.org",
            new TwitchCharityAmount(42000, 2, "EUR"),
            new TwitchCharityAmount(100000, 2, "EUR")
        );

    /// <summary>Seventeen donations in total; the newest is ViewerOne's EUR 10.00.</summary>
    private static TwitchPage<TwitchCharityDonation> Donations() =>
        new(
            [
                new TwitchCharityDonation(
                    "d2",
                    "camp1",
                    "1001",
                    "viewerone",
                    "ViewerOne",
                    new TwitchCharityAmount(1000, 2, "EUR")
                ),
                new TwitchCharityDonation(
                    "d1",
                    "camp1",
                    "1002",
                    "viewertwo",
                    "ViewerTwo",
                    new TwitchCharityAmount(500, 2, "EUR")
                ),
            ],
            null,
            17
        );
}
