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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Dashboard.Replay;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Stoney, 2026-10-27 (live, on stream): "replaying the BSOD reward does not do anything, it should do the whole
/// sequence over." A redemption had no replay presenter, so the dashboard replay found the journaled redemption,
/// ran nothing and answered NOTHING_REPLAYED. These tests drive the real <see cref="ActivityReplayService"/> over a
/// journaled redemption and assert what the replay performed: the reward's pipeline again, the stage effect it
/// reports, and none of the work that belongs to the one real redemption (refund, timer, after-processed event).
/// </summary>
public sealed class RewardRedemptionReplayTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d501");
    private static readonly Guid BsodPipelineId = Guid.Parse(
        "0192a000-0000-7000-8000-00000000d502"
    );
    private static readonly Guid ViewerUserId = Guid.Parse("0192a000-0000-7000-8000-00000000d503");
    private const string ViewerTwitchId = "twitch-viewer-1";
    private const string BsodRewardId = "tw-reward-bsod";
    private const string BsodGraph =
        """{"actions":[{"type":"widget_event","widget":"bsod"},{"type":"play_sound","clip":"error"}]}""";
    private static readonly DateTime RedeemedAt = new(2026, 10, 27, 18, 0, 0, DateTimeKind.Utc);
    private static long _position;

    private sealed record Harness(
        ActivityReplayService Replay,
        RewardRedeemedHandler Handler,
        AuthDbContext Db,
        IPipelineEngine Engine,
        IEventResponseExecutor Executor,
        IRedemptionTimerService Timers,
        IRewardService Rewards,
        RecordingEventBus Bus
    );

    private static Harness Build(int viewerLevel = 0)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Users.Add(
            new()
            {
                Id = ViewerUserId,
                TwitchUserId = ViewerTwitchId,
                Username = "viewer",
                UsernameNormalized = "viewer",
                DisplayName = "Viewer",
            }
        );
        db.SaveChanges();

        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "run-1",
                    Outcome = PipelineOutcome.Completed,
                    Duration = TimeSpan.Zero,
                    StepLogs =
                    [
                        Step(0, "widget_event", succeeded: true),
                        Step(1, "play_sound", succeeded: true),
                        Step(2, "redemption_fulfill", succeeded: false),
                    ],
                }
            );
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        executor
            .ReplayAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new EventResponseOutcome(1, 0, 0));
        IRedemptionTimerService timers = Substitute.For<IRedemptionTimerService>();
        timers
            .StartAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new RedemptionTimerDto(
                        Guid.NewGuid(),
                        "redemption-bsod-1",
                        BsodRewardId,
                        "BSOD",
                        "Viewer",
                        300,
                        300,
                        "running",
                        RedeemedAt
                    )
                )
            );
        IRewardService rewards = Substitute.For<IRewardService>();
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .ResolveEffectiveLevelAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(viewerLevel));

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(executor)
            .AddSingleton(timers)
            .AddSingleton(rewards)
            .AddSingleton(roles)
            .BuildServiceProvider();

        RecordingEventBus bus = new();
        RewardRedeemedHandler handler = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            engine,
            bus,
            NullLogger<RewardRedeemedHandler>.Instance
        );

        JournaledDomainEventReader reader = new(
            new EventPayloadProtector(Substitute.For<ISubjectKeyService>()),
            new EventUpcasterRegistry([]),
            new DomainEventTypeRegistry()
        );
        ActivityReplayService replay = new(
            db,
            reader,
            new GiftBombChainResolver(db, reader),
            [handler],
            Substitute.For<IRenderedAlertReplayer>(),
            new ShoutoutReplayPresenter(
                db,
                Substitute.For<ITwitchChatApi>(),
                Substitute.For<ITtsDispatchService>(),
                Substitute.For<IEventResponseOverlayNotifier>(),
                NullLogger<ShoutoutReplayPresenter>.Instance
            )
        );
        return new(replay, handler, db, engine, executor, timers, rewards, bus);
    }

    private static StepExecutionLog Step(int index, string actionType, bool succeeded) =>
        new()
        {
            StepIndex = index,
            ActionType = actionType,
            Succeeded = succeeded,
            Duration = TimeSpan.Zero,
        };

    private static RewardRedeemedEvent Redemption(string rewardId = BsodRewardId) =>
        new()
        {
            BroadcasterId = Channel,
            RewardId = rewardId,
            RewardTitle = "BSOD",
            RedemptionId = "redemption-bsod-1",
            UserId = ViewerTwitchId,
            UserLogin = "viewer",
            UserDisplayName = "Viewer",
            Cost = 500,
        };

    private static void Journal(AuthDbContext db, RewardRedeemedEvent redemption)
    {
        db.EventJournals.Add(
            new EventJournal
            {
                EventId = redemption.EventId,
                BroadcasterId = Channel,
                StreamPosition = Interlocked.Increment(ref _position),
                EventType = nameof(RewardRedeemedEvent),
                EventVersion = 1,
                Source = "domain",
                Payload = JsonConvert.SerializeObject(redemption),
                Metadata = "{}",
                OccurredAt = RedeemedAt,
                RecordedAt = RedeemedAt,
            }
        );
        db.SaveChanges();
    }

    private static void AddBsodReward(
        Harness h,
        string permission = "everyone",
        bool isEnabled = true,
        int? timerSeconds = null
    )
    {
        h.Db.Pipelines.Add(
            new()
            {
                Id = BsodPipelineId,
                BroadcasterId = Channel,
                Name = "bsod",
                TriggerKind = "reward",
                GraphJsonCache = BsodGraph,
            }
        );
        h.Db.Rewards.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Title = "BSOD",
                TwitchRewardId = BsodRewardId,
                PipelineId = BsodPipelineId,
                Permission = permission,
                IsEnabled = isEnabled,
                TimerDurationSeconds = timerSeconds,
            }
        );
        h.Db.SaveChanges();
    }

    [Fact]
    public void A_reward_redemption_has_a_registered_replay_presenter()
    {
        // The production container registers every concrete IEventResponsePresenter of the assembly
        // (DependencyInjection.AddImplementationsOf); a redemption without one replays nothing.
        List<Type> presenters =
        [
            .. typeof(RewardRedeemedHandler)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsAbstract: false, IsInterface: false }
                    && typeof(IEventResponsePresenter).IsAssignableFrom(t)
                ),
        ];

        presenters.Should().Contain(typeof(RewardRedeemedHandler));
    }

    [Fact]
    public async Task Replaying_a_pipeline_reward_runs_its_whole_pipeline_again_and_reports_the_stage_effects()
    {
        Harness h = Build();
        AddBsodReward(h);
        RewardRedeemedEvent redemption = Redemption();
        Journal(h.Db, redemption);

        Result<ActivityReplayResult> result = await h.Replay.ReplayAsync(
            Channel,
            redemption.EventId.ToString()
        );

        result.IsSuccess.Should().BeTrue();
        // Two stage steps succeeded (the BSOD widget event and the sound); the failed fulfill counts nothing.
        result.Value.OverlaysShown.Should().Be(2);
        result.Value.EventsReplayed.Should().Be(1);
        await h
            .Engine.Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r =>
                    r.BroadcasterId == Channel
                    && r.PipelineId == BsodPipelineId
                    && r.PipelineJson == BsodGraph
                    && r.RewardId == BsodRewardId
                    && r.RedemptionId == "redemption-bsod-1"
                    && r.TriggeredByUserId == ViewerTwitchId
                    && r.InitialVariables["reward"] == "BSOD"
                    && r.InitialVariables["cost"] == "500"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Replaying_the_same_redemption_twice_runs_the_pipeline_twice()
    {
        Harness h = Build();
        AddBsodReward(h);
        RewardRedeemedEvent redemption = Redemption();
        Journal(h.Db, redemption);

        await h.Replay.ReplayAsync(Channel, redemption.EventId.ToString());
        Result<ActivityReplayResult> second = await h.Replay.ReplayAsync(
            Channel,
            redemption.EventId.ToString()
        );

        second.IsSuccess.Should().BeTrue();
        await h
            .Engine.Received(2)
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_replay_hands_the_pipeline_the_same_run_a_fresh_redemption_does()
    {
        Harness live = Build();
        AddBsodReward(live);
        RewardRedeemedEvent redemption = Redemption();
        await live.Handler.HandleAsync(redemption);
        PipelineRequest liveRequest = (PipelineRequest)
            live.Engine.ReceivedCalls().Single().GetArguments()[0]!;

        Harness replayed = Build();
        AddBsodReward(replayed);
        Journal(replayed.Db, redemption);
        await replayed.Replay.ReplayAsync(Channel, redemption.EventId.ToString());
        PipelineRequest replayRequest = (PipelineRequest)
            replayed.Engine.ReceivedCalls().Single().GetArguments()[0]!;

        replayRequest.PipelineId.Should().Be(liveRequest.PipelineId);
        replayRequest.PipelineJson.Should().Be(liveRequest.PipelineJson);
        replayRequest.RedemptionId.Should().Be(liveRequest.RedemptionId);
        replayRequest.RewardId.Should().Be(liveRequest.RewardId);
        replayRequest.TriggeredByUserId.Should().Be(liveRequest.TriggeredByUserId);
        replayRequest.InitialVariables.Should().BeEquivalentTo(liveRequest.InitialVariables);
        // The replayed run is not the original redemption: it must not capture its voice a second time under it.
        liveRequest.ChannelEventId.Should().Be(redemption.EventId.ToString());
        replayRequest.ChannelEventId.Should().BeNull();
    }

    [Fact]
    public async Task A_replay_does_no_work_that_belongs_to_the_one_real_redemption()
    {
        Harness h = Build();
        AddBsodReward(h, timerSeconds: 300);
        RewardRedeemedEvent redemption = Redemption();
        Journal(h.Db, redemption);

        await h.Replay.ReplayAsync(Channel, redemption.EventId.ToString());

        await h
            .Timers.DidNotReceiveWithAnyArgs()
            .StartAsync(default, default!, default!, default!, default!, default, default);
        await h
            .Rewards.DidNotReceiveWithAnyArgs()
            .SetRedemptionStatusAsync(default!, default!, default!, default, default);
        h.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_live_redemption_still_starts_its_timer_and_publishes_the_after_processed_event()
    {
        Harness h = Build();
        AddBsodReward(h, timerSeconds: 300);

        await h.Handler.HandleAsync(Redemption());

        await h
            .Timers.Received(1)
            .StartAsync(
                Channel,
                "redemption-bsod-1",
                BsodRewardId,
                "BSOD",
                "Viewer",
                300,
                Arg.Any<CancellationToken>()
            );
        h.Bus.Published.Should()
            .ContainSingle(e => e is AfterRewardProcessedEvent)
            .Which.Should()
            .BeOfType<AfterRewardProcessedEvent>()
            .Which.RedemptionId.Should()
            .Be("redemption-bsod-1");
    }

    [Fact]
    public async Task A_redemption_the_live_gate_would_have_refused_stays_refused_without_a_second_refund()
    {
        Harness h = Build(viewerLevel: PermissionLevel.Subscriber.ToLevelValue());
        AddBsodReward(h, permission: "moderator");
        RewardRedeemedEvent redemption = Redemption();
        Journal(h.Db, redemption);

        Result<ActivityReplayResult> result = await h.Replay.ReplayAsync(
            Channel,
            redemption.EventId.ToString()
        );

        result.ErrorCode.Should().Be("NOTHING_REPLAYED");
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
        await h
            .Rewards.DidNotReceiveWithAnyArgs()
            .SetRedemptionStatusAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task A_reward_the_broadcaster_disabled_replays_nothing()
    {
        Harness h = Build();
        AddBsodReward(h, isEnabled: false);
        RewardRedeemedEvent redemption = Redemption();
        Journal(h.Db, redemption);

        Result<ActivityReplayResult> result = await h.Replay.ReplayAsync(
            Channel,
            redemption.EventId.ToString()
        );

        result.ErrorCode.Should().Be("NOTHING_REPLAYED");
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
    }

    [Fact]
    public async Task A_reward_with_no_behavior_of_its_own_replays_the_generic_redemption_response()
    {
        Harness h = Build();
        RewardRedeemedEvent redemption = Redemption("tw-unmanaged");
        Journal(h.Db, redemption);

        Result<ActivityReplayResult> result = await h.Replay.ReplayAsync(
            Channel,
            redemption.EventId.ToString()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ChatMessagesSent.Should().Be(1);
        await h
            .Executor.Received(1)
            .ReplayAsync(
                Channel,
                RewardRedeemedSampleSource.ResponseKey,
                ViewerTwitchId,
                "Viewer",
                Arg.Is<Dictionary<string, string>>(v => v["reward"] == "BSOD"),
                Arg.Any<CancellationToken>()
            );
        // A replay is not a new redemption: it never wakes a run parked on the event.
        await h
            .Executor.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default, default!, default, default, default!);
    }
}
