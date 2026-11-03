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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Chat;
using NomNomzBot.Infrastructure.Tests.Supporters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// A test fire runs a sample event through the channel's real event response and its pipeline, presentation
/// only, and reports what was presented. These tests drive the real <see cref="EventResponseTestService"/> over
/// the real executor and template resolver; only the outside world (chat, the pipeline engine, TTS, the overlay
/// presence) is a stand-in.
/// </summary>
public sealed class EventResponseTestServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f3a00-2222-7000-8000-000000000001");
    private static readonly Guid Caller = Guid.Parse("019f3a00-2222-7000-8000-000000000002");

    private sealed record Rig(
        EventResponseTestService Service,
        SupporterTestDbContext Db,
        IChatProvider Chat,
        IPipelineEngine Engine,
        IEventResponseOverlayNotifier Overlay,
        ITtsDispatchService Tts,
        IOverlayPresenceRegistry Presence
    );

    private static async Task<Rig> BuildAsync(bool overlayAttached = true)
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();
        db.Users.Add(
            new()
            {
                Id = Caller,
                TwitchUserId = "4242",
                Username = "stoney_eagle",
                UsernameNormalized = "stoney_eagle",
                DisplayName = "Stoney_Eagle",
            }
        );
        await db.SaveChangesAsync();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        TemplateResolver templates = new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );

        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        IEventResponseOverlayNotifier overlay = Substitute.For<IEventResponseOverlayNotifier>();
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(TtsDispatchDisposition.Dispatched, "v", "p", 1, 1, null)
                )
            );
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsOverlayConnected(Tenant).Returns(overlayAttached);

        EventResponseTestService service = new(
            db,
            engine,
            templates,
            TestReplyDispatcher.OverProvider(chat),
            new OutboundSanctionAccessor(),
            overlay,
            tts,
            Substitute.For<ICommandInvoker>(),
            presence,
            NullLogger<EventResponseExecutor>.Instance
        );
        return new(service, db, chat, engine, overlay, tts, presence);
    }

    private static async Task SeedResponseAsync(
        SupporterTestDbContext db,
        string eventType,
        string responseType,
        string? message = null,
        Guid? pipelineId = null,
        bool enabled = true,
        bool speakWithTts = false
    )
    {
        db.EventResponses.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                EventType = eventType,
                ResponseType = responseType,
                Message = message,
                PipelineId = pipelineId,
                IsEnabled = enabled,
                SpeakWithTts = speakWithTts,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_raid_fires_with_the_caller_as_raider_and_25_viewers_resolved_in_the_chat_line()
    {
        Rig rig = await BuildAsync();
        await SeedResponseAsync(
            rig.Db,
            "channel.raid",
            "chat_message",
            "Welcome {user} and your {viewers} raiders!"
        );

        Result<EventResponseTestResult> result = await rig.Service.FireAsync(
            Tenant,
            "channel.raid",
            Caller
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ChatLine.Should().Be("Welcome Stoney_Eagle and your 25 raiders!");
        result.Value.ChatMessagesSent.Should().Be(1);
        result.Value.Variables["viewers"].Should().Be("25");
        result.Value.Variables["user"].Should().Be("Stoney_Eagle");
        await rig
            .Chat.Received(1)
            .SendMessageAsync(
                Tenant,
                "Welcome Stoney_Eagle and your 25 raiders!",
                Arg.Any<CancellationToken>()
            );
        result.Value.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task A_response_that_speaks_reports_the_spoken_text_as_the_chat_line()
    {
        Rig rig = await BuildAsync();
        await SeedResponseAsync(
            rig.Db,
            "channel.raid",
            "chat_message",
            "Raid by {user}: {viewers}",
            speakWithTts: true
        );

        Result<EventResponseTestResult> result = await rig.Service.FireAsync(
            Tenant,
            "channel.raid",
            Caller
        );

        result.Value.TtsQueued.Should().Be(1);
        result.Value.TtsText.Should().Be("Raid by Stoney_Eagle: 25");
        await rig
            .Tts.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.Text == "Raid by Stoney_Eagle: 25"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_disabled_response_returns_the_warning_and_presents_nothing()
    {
        Rig rig = await BuildAsync();
        await SeedResponseAsync(
            rig.Db,
            "channel.raid",
            "chat_message",
            "Welcome {user}",
            enabled: false
        );

        Result<EventResponseTestResult> result = await rig.Service.FireAsync(
            Tenant,
            "channel.raid",
            Caller
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Warnings.Should().Contain(EventResponseTestWarnings.ResponseDisabled);
        result.Value.ChatLine.Should().BeNull();
        result.Value.ChatMessagesSent.Should().Be(0);
        result.Value.Alerts.Should().BeEmpty();
        result.Value.PipelineSteps.Should().BeEmpty();
        await rig.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
    }

    [Fact]
    public async Task A_channel_with_no_row_for_the_event_is_reported_as_disabled()
    {
        Rig rig = await BuildAsync();

        Result<EventResponseTestResult> result = await rig.Service.FireAsync(
            Tenant,
            "channel.raid",
            Caller
        );

        result.Value.Warnings.Should().Contain(EventResponseTestWarnings.ResponseDisabled);
        await rig.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
    }

    [Fact]
    public async Task No_attached_overlay_browser_source_returns_the_warning_and_an_attached_one_does_not()
    {
        Rig detached = await BuildAsync(overlayAttached: false);
        await SeedResponseAsync(detached.Db, "channel.raid", "chat_message", "Hi {user}");
        Rig attached = await BuildAsync(overlayAttached: true);
        await SeedResponseAsync(attached.Db, "channel.raid", "chat_message", "Hi {user}");

        Result<EventResponseTestResult> none = await detached.Service.FireAsync(
            Tenant,
            "channel.raid",
            Caller
        );
        Result<EventResponseTestResult> some = await attached.Service.FireAsync(
            Tenant,
            "channel.raid",
            Caller
        );

        none.Value.Warnings.Should().Contain(EventResponseTestWarnings.NoOverlaySource);
        some.Value.Warnings.Should().NotContain(EventResponseTestWarnings.NoOverlaySource);
    }

    [Fact]
    public async Task An_overlay_response_reports_the_alert_it_sent_resolved()
    {
        Rig rig = await BuildAsync();
        await SeedResponseAsync(rig.Db, "channel.raid", "overlay", "{user} raids with {viewers}");

        Result<EventResponseTestResult> result = await rig.Service.FireAsync(
            Tenant,
            "channel.raid",
            Caller
        );

        result.Value.OverlaysShown.Should().Be(1);
        result
            .Value.Alerts.Should()
            .ContainSingle()
            .Which.Should()
            .Be("Stoney_Eagle raids with 25");
        await rig
            .Overlay.Received(1)
            .NotifyAsync(
                Tenant,
                "channel.raid",
                "Stoney_Eagle raids with 25",
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_pipeline_response_runs_the_pipeline_with_the_sample_and_reports_each_step()
    {
        Rig rig = await BuildAsync();
        Guid pipelineId = Guid.CreateVersion7();
        rig.Db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = Tenant,
                Name = "Incoming Raid",
                GraphJsonCache = """{"steps":[]}""",
            }
        );
        await rig.Db.SaveChangesAsync();
        await SeedResponseAsync(rig.Db, "channel.raid", "pipeline", pipelineId: pipelineId);
        PipelineRequest? seen = null;
        rig.Engine.ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                seen = call.Arg<PipelineRequest>();
                return Task.FromResult(
                    new PipelineExecutionResult
                    {
                        ExecutionId = "x",
                        Outcome = PipelineOutcome.Completed,
                        Duration = TimeSpan.Zero,
                        StepLogs =
                        [
                            new()
                            {
                                StepIndex = 0,
                                ActionType = "send_message",
                                Succeeded = true,
                                Duration = TimeSpan.Zero,
                            },
                            new()
                            {
                                StepIndex = 1,
                                ActionType = "play_tts",
                                Succeeded = false,
                                Duration = TimeSpan.Zero,
                                ErrorMessage = "no voice",
                            },
                        ],
                    }
                );
            });

        Result<EventResponseTestResult> result = await rig.Service.FireAsync(
            Tenant,
            "channel.raid",
            Caller
        );

        seen.Should().NotBeNull();
        seen!.InitialVariables["viewers"].Should().Be("25");
        seen.InitialVariables["event.name"].Should().Be("channel.raid");
        seen.TriggeredByDisplayName.Should().Be("Stoney_Eagle");
        result.Value.PipelineSteps.Should().HaveCount(2);
        result
            .Value.PipelineSteps[0]
            .Should()
            .Be(new EventResponseTestStep(0, "send_message", true, null));
        result
            .Value.PipelineSteps[1]
            .Should()
            .Be(new EventResponseTestStep(1, "play_tts", false, "no voice"));
        result.Value.Warnings.Should().NotContain(EventResponseTestWarnings.NoPipeline);
    }

    [Fact]
    public async Task A_pipeline_response_with_no_pipeline_returns_the_warning()
    {
        Rig rig = await BuildAsync();
        await SeedResponseAsync(rig.Db, "channel.raid", "pipeline", pipelineId: null);

        Result<EventResponseTestResult> result = await rig.Service.FireAsync(
            Tenant,
            "channel.raid",
            Caller
        );

        result.Value.Warnings.Should().Contain(EventResponseTestWarnings.NoPipeline);
        result.Value.PipelineSteps.Should().BeEmpty();
        await rig.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task A_test_fire_is_presentation_only_it_writes_no_row_and_wakes_no_parked_run()
    {
        Rig rig = await BuildAsync();
        await SeedResponseAsync(rig.Db, "channel.raid", "chat_message", "Hi {user}");
        int activityBefore = await rig.Db.ChannelEvents.CountAsync();
        rig.Db.ChangeTracker.Clear();

        await rig.Service.FireAsync(Tenant, "channel.raid", Caller);

        (await rig.Db.ChannelEvents.CountAsync()).Should().Be(activityBefore);
        rig.Db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Should()
            .BeEmpty();
        await rig
            .Engine.DidNotReceiveWithAnyArgs()
            .ResumeSuspendedRunsForEventAsync(default, default!, default!, default);
    }

    [Fact]
    public void The_service_reaches_no_currency_loyalty_counter_stats_hype_or_journal_service()
    {
        string[] forbidden =
        [
            "Currency",
            "Loyalty",
            "Counter",
            "Stats",
            "Hype",
            "Journal",
            "Activity",
            "EventBus",
            "Reward",
        ];

        IEnumerable<string> dependencies = typeof(EventResponseTestService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType.Name);

        foreach (string dependency in dependencies)
            forbidden.Should().NotContain(f => dependency.Contains(f), dependency);
    }

    [Fact]
    public async Task An_event_type_outside_the_response_table_is_refused()
    {
        Rig rig = await BuildAsync();

        Result<EventResponseTestResult> result = await rig.Service.FireAsync(
            Tenant,
            "channel.not_a_real_event",
            Caller
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
