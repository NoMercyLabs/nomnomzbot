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

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Discord.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// The <c>poll_end</c>, <c>prediction_resolved</c> and <c>resubscription</c> Discord triggers, each raised as
/// its real domain event against the real dispatcher: the test reads back the Discord channel, the rendered
/// text and the dedupe row.
/// </summary>
public sealed class DiscordPollPredictionResubHandlerTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 22, 20, 0, 0, TimeSpan.Zero);

    private static DiscordPollEndNotificationHandler PollEnd(
        IDiscordNotificationDispatcher dispatcher
    ) =>
        new(
            dispatcher,
            new OutboundSanctionAccessor(),
            NullLogger<DiscordPollEndNotificationHandler>.Instance
        );

    private static DiscordPredictionResolvedNotificationHandler PredictionResolved(
        IDiscordNotificationDispatcher dispatcher
    ) =>
        new(
            dispatcher,
            new OutboundSanctionAccessor(),
            NullLogger<DiscordPredictionResolvedNotificationHandler>.Instance
        );

    private static DiscordResubscriptionNotificationHandler Resub(
        IDiscordNotificationDispatcher dispatcher
    ) =>
        new(
            dispatcher,
            new OutboundSanctionAccessor(),
            NullLogger<DiscordResubscriptionNotificationHandler>.Instance
        );

    private static PollEndedEvent Poll(
        Guid broadcaster,
        string status = "completed",
        string? winningChoiceId = "c2",
        string pollId = "poll-1"
    ) =>
        new()
        {
            BroadcasterId = broadcaster,
            OccurredAt = At,
            PollId = pollId,
            Title = "Which game next?",
            Status = status,
            Choices = [new("c1", "Elden Ring", 14, 3), new("c2", "Hades II", 31, 8)],
            WinningChoiceId = winningChoiceId,
        };

    private static PredictionEndedEvent Prediction(
        Guid broadcaster,
        string status = "resolved",
        string? winningOutcomeId = "o1",
        string predictionId = "pred-1"
    ) =>
        new()
        {
            BroadcasterId = broadcaster,
            OccurredAt = At,
            PredictionId = predictionId,
            Title = "Will I beat the boss?",
            Status = status,
            Outcomes = [new("o1", "Yes", 5200, 18, "blue"), new("o2", "No", 2400, 9, "pink")],
            WinningOutcomeId = winningOutcomeId,
        };

    private static ResubscriptionEvent ResubOf(Guid broadcaster, int months = 7) =>
        new()
        {
            BroadcasterId = broadcaster,
            OccurredAt = At,
            UserId = "12",
            UserDisplayName = "Loyal",
            Tier = "3000",
            CumulativeMonths = months,
            StreakMonths = 4,
            Message = "still here",
        };

    private static async Task AssertOnePostAsync(
        DiscordTriggerHandlerHarness harness,
        string trigger,
        string text,
        string dedupeKey
    )
    {
        harness.Gateway.Posts.Should().ContainSingle();
        harness.Gateway.Posts[0].ChannelId.Should().Be(DiscordTriggerHandlerHarness.DiscordChannel);
        harness.Gateway.Posts[0].Message.Content.Should().Be(text);
        DiscordNotificationDispatch row = (await harness.DispatchesAsync()).Single();
        row.TriggerType.Should().Be(trigger);
        row.Status.Should().Be("sent");
        row.DedupeKey.Should().Be(dedupeKey);
    }

    // ── poll end ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PollEnd_PostsTheRule_WithTitleWinnerVotesAndResults()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync(
                "poll_end",
                "{poll.title}: {poll.winner} won with {poll.winner.votes} ({poll.results})"
            );

        await harness.RaiseAsync(PollEnd, Poll(harness.BroadcasterId));

        await AssertOnePostAsync(
            harness,
            "poll_end",
            "Which game next?: Hades II won with 31 (Elden Ring: 14 | Hades II: 31)",
            "poll_end:poll-1"
        );
    }

    [Fact]
    public async Task PollEnd_WithNoVotes_NamesNobodyAsWinner()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync(
                "poll_end",
                "winner: {poll.winner} ({poll.winner.votes})"
            );

        await harness.RaiseAsync(PollEnd, Poll(harness.BroadcasterId, winningChoiceId: null));

        harness.Gateway.Posts.Single().Message.Content.Should().Be("winner: nobody (0)");
    }

    [Theory]
    [InlineData("terminated")]
    [InlineData("archived")]
    public async Task PollEnd_ThatWasCutShort_StaysSilent(string status)
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("poll_end", "done");

        await harness.RaiseAsync(PollEnd, Poll(harness.BroadcasterId, status));

        harness.Gateway.Posts.Should().BeEmpty();
        (await harness.DispatchesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PollEnd_DeliveredTwice_PostsOnce()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("poll_end", "done");
        PollEndedEvent first = Poll(harness.BroadcasterId);
        PollEndedEvent second = Poll(harness.BroadcasterId);

        await harness.RaiseAsync(PollEnd, first);
        await harness.RaiseAsync(PollEnd, second);

        harness.Gateway.Posts.Should().ContainSingle();
        (await harness.DispatchesAsync())
            .Select(row => row.Status)
            .Should()
            .Equal("sent", "skipped_dupe");
    }

    [Fact]
    public async Task PollEnd_TwoDifferentPolls_PostTwice()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("poll_end", "done");

        await harness.RaiseAsync(PollEnd, Poll(harness.BroadcasterId, pollId: "poll-1"));
        await harness.RaiseAsync(PollEnd, Poll(harness.BroadcasterId, pollId: "poll-2"));

        harness.Gateway.Posts.Should().HaveCount(2);
    }

    // ── prediction resolved ──────────────────────────────────────────────────

    [Fact]
    public async Task PredictionResolved_PostsTheRule_WithTitleAndWinningOutcome()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync(
                "prediction_resolved",
                "{prediction.title} -> {prediction.winner} ({prediction.winner.points} points, {prediction.winner.users} viewers)"
            );

        await harness.RaiseAsync(PredictionResolved, Prediction(harness.BroadcasterId));

        await AssertOnePostAsync(
            harness,
            "prediction_resolved",
            "Will I beat the boss? -> Yes (5200 points, 18 viewers)",
            "prediction_resolved:pred-1"
        );
    }

    [Fact]
    public async Task PredictionResolved_ThatWasCancelled_StaysSilent()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("prediction_resolved", "done");

        await harness.RaiseAsync(
            PredictionResolved,
            Prediction(harness.BroadcasterId, "canceled", winningOutcomeId: null)
        );

        harness.Gateway.Posts.Should().BeEmpty();
        (await harness.DispatchesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PredictionResolved_DeliveredTwice_PostsOnce()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("prediction_resolved", "done");

        await harness.RaiseAsync(PredictionResolved, Prediction(harness.BroadcasterId));
        await harness.RaiseAsync(PredictionResolved, Prediction(harness.BroadcasterId));

        harness.Gateway.Posts.Should().ContainSingle();
        (await harness.DispatchesAsync())
            .Select(row => row.Status)
            .Should()
            .Equal("sent", "skipped_dupe");
    }

    // ── resubscription ───────────────────────────────────────────────────────

    [Fact]
    public async Task Resub_PostsTheRule_WithNameTierMonthsStreakAndMessage()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync(
                "resubscription",
                "{user} resubbed ({tier}) for {months} months, streak {streak}: {message}"
            );

        await harness.RaiseAsync(Resub, ResubOf(harness.BroadcasterId));

        await AssertOnePostAsync(
            harness,
            "resubscription",
            "Loyal resubbed (Tier 3) for 7 months, streak 4: still here",
            $"resubscription:twitch:12:{At.UtcDateTime:O}:7"
        );
    }

    [Fact]
    public async Task Resub_DeliveredTwice_PostsOnce()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("resubscription", "again");

        await harness.RaiseAsync(Resub, ResubOf(harness.BroadcasterId));
        await harness.RaiseAsync(Resub, ResubOf(harness.BroadcasterId));

        harness.Gateway.Posts.Should().ContainSingle();
        (await harness.DispatchesAsync())
            .Select(row => row.Status)
            .Should()
            .Equal("sent", "skipped_dupe");
    }

    [Fact]
    public async Task Resub_ForAnotherMonthCount_PostsAgain()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("resubscription", "again");

        await harness.RaiseAsync(Resub, ResubOf(harness.BroadcasterId, months: 7));
        await harness.RaiseAsync(Resub, ResubOf(harness.BroadcasterId, months: 8));

        harness.Gateway.Posts.Should().HaveCount(2);
    }

    // ── no rule, no post ─────────────────────────────────────────────────────

    [Fact]
    public async Task WithoutARuleForTheTrigger_NothingIsPosted()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("go_live", "live!");

        await harness.RaiseAsync(PollEnd, Poll(harness.BroadcasterId));
        await harness.RaiseAsync(PredictionResolved, Prediction(harness.BroadcasterId));
        await harness.RaiseAsync(Resub, ResubOf(harness.BroadcasterId));

        harness.Gateway.Posts.Should().BeEmpty();
        (await harness.DispatchesAsync()).Should().BeEmpty();
    }

    // ── catalog ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("resubscription", "user", "tier", "months", "streak", "message", "channel.display")]
    [InlineData(
        "poll_end",
        "poll.title",
        "poll.winner",
        "poll.winner.votes",
        "poll.results",
        "channel.display"
    )]
    [InlineData(
        "prediction_resolved",
        "prediction.title",
        "prediction.winner",
        "prediction.winner.points",
        "prediction.winner.users",
        "channel.display"
    )]
    public void EachTrigger_IsInTheCatalog_WithItsLabelAndPlaceholders(
        string name,
        params string[] placeholders
    )
    {
        DiscordNotificationTrigger trigger =
            DiscordConfigDefaults.NotificationTriggerCatalog.Single(entry => entry.Name == name);

        trigger.LabelKey.Should().Be($"dashboard.discord.triggers.{name}");
        trigger.Placeholders.Should().Equal(placeholders);
    }
}
