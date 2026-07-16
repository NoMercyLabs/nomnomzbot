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
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Supporters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// Proves the ONE event-response execution path every trigger source dispatches through: an enabled
/// <c>chat_message</c> row sends the RESOLVED template; an enabled <c>pipeline</c> row runs the bound
/// pipeline's cached graph with the trigger's variables and attribution; a disabled row, a <c>none</c>
/// row, a blank template, or a dangling pipeline id all do nothing — and an executor failure never
/// escapes into the caller (the event bus must not see it).
/// </summary>
public sealed class EventResponseExecutorTests
{
    private static readonly Guid Tenant = Guid.Parse("019f3a00-1111-7000-8000-000000000001");

    private static (
        EventResponseExecutor Executor,
        SupporterTestDbContext Db,
        IChatProvider Chat,
        IPipelineEngine Engine
    ) Build()
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

        EventResponseExecutor executor = new(
            db,
            engine,
            templates,
            chat,
            NullLogger<EventResponseExecutor>.Instance
        );
        return (executor, db, chat, engine);
    }

    private static async Task SeedResponseAsync(
        SupporterTestDbContext db,
        string eventType,
        string responseType,
        string? message = null,
        Guid? pipelineId = null,
        bool enabled = true
    )
    {
        db.EventResponses.Add(
            new EventResponse
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                EventType = eventType,
                ResponseType = responseType,
                Message = message,
                PipelineId = pipelineId,
                IsEnabled = enabled,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task An_enabled_chat_message_row_sends_the_resolved_template()
    {
        (EventResponseExecutor executor, SupporterTestDbContext db, IChatProvider chat, _) =
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

    [Fact]
    public async Task An_enabled_pipeline_row_runs_the_cached_graph_with_the_triggers_variables()
    {
        (
            EventResponseExecutor executor,
            SupporterTestDbContext db,
            IChatProvider chat,
            IPipelineEngine engine
        ) = Build();
        Guid pipelineId = Guid.CreateVersion7();
        db.Pipelines.Add(
            new NomNomzBot.Domain.Commands.Entities.Pipeline
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
                    && r.InitialVariables!["title"] == "Birds"
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
            IPipelineEngine engine
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
    public async Task A_blank_template_or_dangling_pipeline_id_does_nothing()
    {
        (
            EventResponseExecutor executor,
            SupporterTestDbContext db,
            IChatProvider chat,
            IPipelineEngine engine
        ) = Build();
        await SeedResponseAsync(db, "channel.follow", "chat_message", message: "   ");
        await SeedResponseAsync(
            db,
            "channel.cheer",
            "pipeline",
            pipelineId: Guid.CreateVersion7() // no such Pipeline row
        );

        await executor.ExecuteAsync(Tenant, "channel.follow", null, null, []);
        await executor.ExecuteAsync(Tenant, "channel.cheer", null, null, []);

        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
        await engine
            .DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_send_failure_is_swallowed_never_thrown_into_the_event_bus()
    {
        (EventResponseExecutor executor, SupporterTestDbContext db, IChatProvider chat, _) =
            Build();
        await SeedResponseAsync(db, "stream.online", "chat_message", message: "boom");
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("chat down"));

        Func<Task> act = () => executor.ExecuteAsync(Tenant, "stream.online", null, null, []);

        await act.Should().NotThrowAsync();
    }
}
