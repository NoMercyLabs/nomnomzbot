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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Domain.Community.Entities;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Infrastructure.Community;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.Content.PlatformContent;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Community;

/// <summary>
/// Proves the poll/prediction history folds the journal's begin and end events into one row per poll or
/// prediction, with the final result, newest first, scoped to the channel.
/// </summary>
public sealed class PollPredictionHistoryServiceTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 18, 0, 0, TimeSpan.Zero);

    private static EventStoreSubscriber NewSubscriber(ViewerProfileServiceTestDbContext db) =>
        new(
            new EventJournalService(
                db,
                new CountingTenantSequenceAllocator(),
                new TestUnitOfWork(db),
                TimeProvider.System,
                new PassthroughEventPayloadProtector(),
                Substitute.For<ICurrentUserService>()
            ),
            new EventUpcasterRegistry([])
        );

    private static PollBeganEvent PollBegan(string id, DateTimeOffset at, Guid channel) =>
        new()
        {
            BroadcasterId = channel,
            OccurredAt = at,
            PollId = id,
            Title = "Best snack?",
            Choices = [new("c1", "Chips", 0, 0), new("c2", "Fruit", 0, 0)],
            DurationSeconds = 60,
            EndsAt = at.AddSeconds(60),
        };

    private static PollEndedEvent PollEnded(string id, DateTimeOffset at, Guid channel) =>
        new()
        {
            BroadcasterId = channel,
            OccurredAt = at,
            PollId = id,
            Title = "Best snack?",
            Status = "completed",
            Choices = [new("c1", "Chips", 30, 10), new("c2", "Fruit", 50, 10)],
            WinningChoiceId = "c2",
        };

    [Fact]
    public async Task ListAsync_FoldsBeginAndEnd_IntoOnePollRowWithFinalResult()
    {
        ViewerProfileServiceTestDbContext db = ViewerProfileServiceTestDbContext.New();
        EventStoreSubscriber subscriber = NewSubscriber(db);
        (await subscriber.CaptureAsync(PollBegan("p1", T0, Broadcaster)))
            .IsSuccess.Should()
            .BeTrue();
        (await subscriber.CaptureAsync(PollEnded("p1", T0.AddMinutes(1), Broadcaster)))
            .IsSuccess.Should()
            .BeTrue();

        Result<IReadOnlyList<PollPredictionHistoryItemDto>> result =
            await new PollPredictionHistoryService(db).ListAsync(Broadcaster);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        PollPredictionHistoryItemDto item = result.Value.Should().ContainSingle().Subject;
        item.Kind.Should().Be("poll");
        item.Status.Should().Be("completed");
        item.StartedAt.Should().Be(T0);
        item.EndedAt.Should().Be(T0.AddMinutes(1));
        item.TotalVotes.Should().Be(80);
        item.Options.Select(o => (o.Title, o.Votes, o.SharePercent, o.IsWinner))
            .Should()
            .Equal(("Chips", 30, 38, false), ("Fruit", 50, 63, true));
    }

    [Fact]
    public async Task ListAsync_FoldsPredictionOutcomes_WithUsersAsVotesAndPointsAsChannelPoints()
    {
        ViewerProfileServiceTestDbContext db = ViewerProfileServiceTestDbContext.New();
        EventStoreSubscriber subscriber = NewSubscriber(db);
        (
            await subscriber.CaptureAsync(
                new PredictionBeganEvent
                {
                    BroadcasterId = Broadcaster,
                    OccurredAt = T0,
                    PredictionId = "x1",
                    Title = "Win the run?",
                    Outcomes = [new("o1", "Yes", 0, 0, "blue"), new("o2", "No", 0, 0, "pink")],
                    WindowSeconds = 120,
                    LocksAt = T0.AddSeconds(120),
                }
            )
        ).IsSuccess.Should().BeTrue();
        (
            await subscriber.CaptureAsync(
                new PredictionEndedEvent
                {
                    BroadcasterId = Broadcaster,
                    OccurredAt = T0.AddMinutes(10),
                    PredictionId = "x1",
                    Title = "Win the run?",
                    Status = "resolved",
                    Outcomes = [new("o1", "Yes", 900, 3, "blue"), new("o2", "No", 100, 1, "pink")],
                    WinningOutcomeId = "o1",
                }
            )
        ).IsSuccess.Should().BeTrue();

        Result<IReadOnlyList<PollPredictionHistoryItemDto>> result =
            await new PollPredictionHistoryService(db).ListAsync(Broadcaster);

        PollPredictionHistoryItemDto item = result.Value.Should().ContainSingle().Subject;
        item.Kind.Should().Be("prediction");
        item.Status.Should().Be("resolved");
        item.TotalVotes.Should().Be(4);
        item.Options.Select(o => (o.Title, o.Votes, o.ChannelPoints, o.SharePercent, o.IsWinner))
            .Should()
            .Equal(("Yes", 3, 900, 75, true), ("No", 1, 100, 25, false));
    }

    [Fact]
    public async Task ListAsync_ShowsStartWithoutEnd_AsUnfinished_NewestFirst_AndIgnoresOtherChannels()
    {
        ViewerProfileServiceTestDbContext db = ViewerProfileServiceTestDbContext.New();
        Guid other = Guid.CreateVersion7();
        EventStoreSubscriber subscriber = NewSubscriber(db);
        (await subscriber.CaptureAsync(PollBegan("old", T0, Broadcaster)))
            .IsSuccess.Should()
            .BeTrue();
        (await subscriber.CaptureAsync(PollEnded("old", T0.AddMinutes(1), Broadcaster)))
            .IsSuccess.Should()
            .BeTrue();
        (await subscriber.CaptureAsync(PollBegan("lost", T0.AddHours(1), Broadcaster)))
            .IsSuccess.Should()
            .BeTrue();
        (await subscriber.CaptureAsync(PollBegan("theirs", T0.AddHours(2), other)))
            .IsSuccess.Should()
            .BeTrue();
        (await subscriber.CaptureAsync(PollEnded("theirs", T0.AddHours(2), other)))
            .IsSuccess.Should()
            .BeTrue();

        Result<IReadOnlyList<PollPredictionHistoryItemDto>> result =
            await new PollPredictionHistoryService(db).ListAsync(Broadcaster);

        result
            .Value.Select(i => (i.Id, i.Status))
            .Should()
            .Equal(("lost", "unfinished"), ("old", "completed"));
        result.Value[0].EndedAt.Should().BeNull();
        result.Value[0].Options.Select(o => o.Title).Should().Equal("Chips", "Fruit");
    }

    [Fact]
    public async Task ListAsync_IncludesTheBotsChatPolls_WithCountedVotesAndTheTopOptionAsWinnerOnceClosed()
    {
        ViewerProfileServiceTestDbContext db = ViewerProfileServiceTestDbContext.New();
        Guid closedId = Guid.CreateVersion7();
        Guid openId = Guid.CreateVersion7();
        db.ChatPolls.AddRange(
            new ChatPoll
            {
                Id = closedId,
                BroadcasterId = Broadcaster,
                Question = "Map?",
                OptionsJson = JsonConvert.SerializeObject(new[] { "Forest", "Desert", "Cave" }),
                Status = ChatPollStatus.Closed,
                OpenedAt = T0.UtcDateTime,
                ClosedAt = T0.AddMinutes(2).UtcDateTime,
            },
            new ChatPoll
            {
                Id = openId,
                BroadcasterId = Broadcaster,
                Question = "Music?",
                OptionsJson = JsonConvert.SerializeObject(new[] { "Yes", "No" }),
                Status = ChatPollStatus.Open,
                OpenedAt = T0.AddHours(1).UtcDateTime,
            }
        );
        db.ChatPollVotes.AddRange(
            Vote(closedId, "a", 2),
            Vote(closedId, "b", 2),
            Vote(closedId, "c", 1),
            Vote(openId, "a", 1)
        );
        await db.SaveChangesAsync();

        Result<IReadOnlyList<PollPredictionHistoryItemDto>> result =
            await new PollPredictionHistoryService(db).ListAsync(Broadcaster);

        result
            .Value.Select(i => (i.Kind, i.Title, i.Status))
            .Should()
            .Equal(("chat-poll", "Music?", "open"), ("chat-poll", "Map?", "closed"));
        PollPredictionHistoryItemDto closed = result.Value[1];
        closed.EndedAt.Should().Be(T0.AddMinutes(2));
        closed.TotalVotes.Should().Be(3);
        closed
            .Options.Select(o => (o.Title, o.Votes, o.SharePercent, o.IsWinner))
            .Should()
            .Equal(("Forest", 1, 33, false), ("Desert", 2, 67, true), ("Cave", 0, 0, false));
        result.Value[0].Options.Should().OnlyContain(o => !o.IsWinner);
    }

    private static ChatPollVote Vote(Guid pollId, string voter, int option) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Broadcaster,
            PollId = pollId,
            VoterUserId = voter,
            VoterProvider = "twitch",
            OptionIndex = option,
            VotedAt = T0.UtcDateTime,
        };
}
