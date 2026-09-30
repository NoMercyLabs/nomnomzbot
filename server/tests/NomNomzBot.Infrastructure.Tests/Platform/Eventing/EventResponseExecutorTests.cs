// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Supporters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// Proves the ONE event-response execution path every trigger source dispatches through: an enabled
/// <c>chat_message</c> row sends the RESOLVED template; an enabled <c>pipeline</c> row runs the bound
/// pipeline's cached graph with the trigger's variables and attribution; an <c>overlay</c> row pushes the
/// resolved message + metadata to the overlay notifier and never posts to chat; a disabled row, a
/// <c>none</c> row, a blank template, a disabled pipeline, or a dangling pipeline id all do nothing — and
/// an executor failure never escapes into the caller (the event bus must not see it).
/// </summary>
public sealed class EventResponseExecutorTests
{
    private static readonly Guid Tenant = Guid.Parse("019f3a00-1111-7000-8000-000000000001");

    private static (
        EventResponseExecutor Executor,
        SupporterTestDbContext Db,
        IChatProvider Chat,
        IPipelineEngine Engine,
        IEventResponseOverlayNotifier Overlay
    ) Build(ITtsDispatchService? tts = null)
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();

        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult($"resolved:{callInfo.ArgAt<string>(0)}"));

        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        IEventResponseOverlayNotifier overlay = Substitute.For<IEventResponseOverlayNotifier>();

        EventResponseExecutor executor = new(
            db,
            engine,
            templates,
            chat,
            overlay,
            tts ?? Substitute.For<ITtsDispatchService>(),
            NullLogger<EventResponseExecutor>.Instance
        );
        return (executor, db, chat, engine, overlay);
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
    public async Task An_enabled_chat_message_row_sends_the_resolved_template()
    {
        (EventResponseExecutor executor, SupporterTestDbContext db, IChatProvider chat, _, _) =
            Build();
        await SeedResponseAsync(db, "stream.online", "chat_message", message: "We're live!");

        await executor.ExecuteAsync(
            Tenant,
            "stream.online",
            userId: null,
            userDisplayName: "Streamer",
            new(StringComparer.OrdinalIgnoreCase) { ["title"] = "Birds" }
        );

        await chat.Received(1)
            .SendMessageAsync(Tenant, "resolved:We're live!", Arg.Any<CancellationToken>());
    }

    private static readonly Dictionary<string, string> GiftVariables = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["user"] = "Gifter",
        ["count"] = "5",
    };

    [Fact]
    public async Task A_chat_message_row_with_tts_sends_chat_and_speaks_the_same_resolved_text_in_the_channel_voice()
    {
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "voice",
                        "edge",
                        10,
                        1000,
                        null
                    )
                )
            );
        (EventResponseExecutor executor, SupporterTestDbContext db, IChatProvider chat, _, _) =
            Build(tts);
        await SeedResponseAsync(
            db,
            "channel.subscription.gift",
            "chat_message",
            message: "{user} gifted {count} subs!",
            speakWithTts: true
        );

        await executor.ExecuteAsync(
            Tenant,
            "channel.subscription.gift",
            userId: "123",
            userDisplayName: "Gifter",
            GiftVariables
        );

        await chat.Received(1)
            .SendMessageAsync(
                Tenant,
                "resolved:{user} gifted {count} subs!",
                Arg.Any<CancellationToken>()
            );
        await tts.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.BroadcasterId == Tenant
                    && r.Text == "resolved:{user} gifted {count} subs!"
                    && r.RequestedByTwitchUserId == string.Empty
                    && r.VoiceIdOverride == null
                    && r.CommunityStanding == "broadcaster"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_chat_message_row_without_tts_never_reaches_tts()
    {
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        (EventResponseExecutor executor, SupporterTestDbContext db, IChatProvider chat, _, _) =
            Build(tts);
        await SeedResponseAsync(
            db,
            "channel.subscription.gift",
            "chat_message",
            message: "{user} gifted {count} subs!"
        );

        await executor.ExecuteAsync(
            Tenant,
            "channel.subscription.gift",
            userId: "123",
            userDisplayName: "Gifter",
            GiftVariables
        );

        await chat.Received(1)
            .SendMessageAsync(Tenant, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);
    }

    [Fact]
    public async Task A_tts_rejection_or_crash_still_sends_the_chat_message()
    {
        ITtsDispatchService rejecting = Substitute.For<ITtsDispatchService>();
        rejecting
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TtsDispatchOutcome>(
                    "TTS is disabled for this channel.",
                    "FEATURE_DISABLED"
                )
            );
        ITtsDispatchService crashing = Substitute.For<ITtsDispatchService>();
        crashing
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<TtsDispatchOutcome>>>(_ =>
                throw new InvalidOperationException("synth down")
            );

        foreach (ITtsDispatchService tts in new[] { rejecting, crashing })
        {
            (EventResponseExecutor executor, SupporterTestDbContext db, IChatProvider chat, _, _) =
                Build(tts);
            await SeedResponseAsync(
                db,
                "channel.subscription.gift.received",
                "chat_message",
                message: "Thanks for the gift!",
                speakWithTts: true
            );

            Func<Task> act = () =>
                executor.ExecuteAsync(
                    Tenant,
                    "channel.subscription.gift.received",
                    userId: "123",
                    userDisplayName: "Gifter",
                    GiftVariables
                );

            await act.Should().NotThrowAsync();
            await chat.Received(1)
                .SendMessageAsync(
                    Tenant,
                    "resolved:Thanks for the gift!",
                    Arg.Any<CancellationToken>()
                );
            await tts.Received(1)
                .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task An_enabled_pipeline_row_runs_the_cached_graph_with_the_triggers_variables()
    {
        (
            EventResponseExecutor executor,
            SupporterTestDbContext db,
            IChatProvider chat,
            IPipelineEngine engine,
            _
        ) = Build();
        Guid pipelineId = Guid.CreateVersion7();
        db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = Tenant,
                Name = "online flow",
                GraphJsonCache = """{"steps":[]}""",
            }
        );
        await db.SaveChangesAsync();
        await SeedResponseAsync(db, "stream.online", "pipeline", pipelineId: pipelineId);

        await executor.ExecuteAsync(
            Tenant,
            "stream.online",
            userId: "42",
            userDisplayName: "Streamer",
            new(StringComparer.OrdinalIgnoreCase) { ["title"] = "Birds" }
        );

        await engine
            .Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r =>
                    r.BroadcasterId == Tenant
                    && r.PipelineId == pipelineId
                    && r.PipelineJson == """{"steps":[]}"""
                    && r.TriggeredByUserId == "42"
                    && r.TriggeredByDisplayName == "Streamer"
                    && r.InitialVariables["title"] == "Birds"
                ),
                Arg.Any<CancellationToken>()
            );
        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, "chat_message", "configured but disabled")]
    [InlineData(true, "none", "explicitly set to do nothing")]
    public async Task A_row_that_must_not_fire_does_nothing(
        bool enabled,
        string responseType,
        string because
    )
    {
        (
            EventResponseExecutor executor,
            SupporterTestDbContext db,
            IChatProvider chat,
            IPipelineEngine engine,
            _
        ) = Build();
        await SeedResponseAsync(db, "stream.online", responseType, "hi chat", enabled: enabled);

        await executor.ExecuteAsync(Tenant, "stream.online", null, null, []);

        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
        await engine
            .DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, Arg.Any<CancellationToken>());
        because.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_blank_template_on_an_event_without_tone_lines_or_a_dangling_pipeline_id_does_nothing()
    {
        (
            EventResponseExecutor executor,
            SupporterTestDbContext db,
            IChatProvider chat,
            IPipelineEngine engine,
            _
        ) = Build();
        // stream.online has no tone lines, so a blank own row has nothing to pick (a follow would speak its tone).
        await SeedResponseAsync(db, "stream.online", "chat_message", message: "   ");
        await SeedResponseAsync(
            db,
            "channel.cheer",
            "pipeline",
            pipelineId: Guid.CreateVersion7() // no such Pipeline row
        );

        await executor.ExecuteAsync(Tenant, "stream.online", null, null, []);
        await executor.ExecuteAsync(Tenant, "channel.cheer", null, null, []);

        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
        await engine
            .DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_enabled_overlay_row_notifies_the_overlay_and_never_posts_to_chat()
    {
        (
            EventResponseExecutor executor,
            SupporterTestDbContext db,
            IChatProvider chat,
            IPipelineEngine engine,
            IEventResponseOverlayNotifier overlay
        ) = Build();
        await SeedResponseAsync(db, "channel.follow", "overlay", message: "{{user}} followed!");

        await executor.ExecuteAsync(
            Tenant,
            "channel.follow",
            userId: "9",
            userDisplayName: "Newcomer",
            new(StringComparer.OrdinalIgnoreCase) { ["user"] = "Newcomer" }
        );

        await overlay
            .Received(1)
            .NotifyAsync(
                Tenant,
                "channel.follow",
                "resolved:{{user}} followed!",
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
        await engine
            .DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_disabled_pipeline_bound_to_an_enabled_event_response_does_not_run()
    {
        (
            EventResponseExecutor executor,
            SupporterTestDbContext db,
            IChatProvider chat,
            IPipelineEngine engine,
            _
        ) = Build();
        Guid pipelineId = Guid.CreateVersion7();
        db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = Tenant,
                Name = "disabled flow",
                GraphJsonCache = """{"steps":[]}""",
                IsEnabled = false,
            }
        );
        await db.SaveChangesAsync();
        await SeedResponseAsync(db, "stream.online", "pipeline", pipelineId: pipelineId);

        await executor.ExecuteAsync(Tenant, "stream.online", null, null, []);

        await engine
            .DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, Arg.Any<CancellationToken>());
        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_send_failure_is_swallowed_never_thrown_into_the_event_bus()
    {
        (EventResponseExecutor executor, SupporterTestDbContext db, IChatProvider chat, _, _) =
            Build();
        await SeedResponseAsync(db, "stream.online", "chat_message", message: "boom");
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("chat down"));

        Func<Task> act = () => executor.ExecuteAsync(Tenant, "stream.online", null, null, []);

        await act.Should().NotThrowAsync();
    }
}
