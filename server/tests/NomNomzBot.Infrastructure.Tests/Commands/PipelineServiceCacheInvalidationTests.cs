// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// S017: a bound <c>Command</c>/<c>ChatTrigger</c> resolves its pipeline's steps ONCE, at
/// <see cref="ChannelContext"/> cache-load time, into <see cref="CachedCommand.PipelineGraphJson"/> /
/// <see cref="CachedChatTrigger.PipelineGraphJson"/> — <see cref="PipelineService"/> never touched that cache
/// on create/update/delete, so an edited pipeline kept running its old graph and a deleted one kept firing until
/// a restart. These tests prove the fix by driving <see cref="IChannelRegistry.InvalidateCommandsAsync"/> /
/// <see cref="IChannelRegistry.InvalidateChatTriggersAsync"/> to actually perform a reload (exactly what the
/// real <c>ChannelRegistry</c> does) and then reading back through <see cref="ChannelContext.Commands"/> /
/// <see cref="ChannelContext.ChatTriggers"/> — the SAME dictionaries <c>ChatMessageHandler</c> looks up on the
/// hot path — rather than merely asserting the invalidation method was called.
/// </summary>
public sealed class PipelineServiceCacheInvalidationTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000c0801");
    private static readonly Guid ChannelB = Guid.Parse("0192a000-0000-7000-8000-0000000c0802");

    private sealed class FakeAction : ICommandAction
    {
        public required string ActionType { get; init; }

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    private static object GraphWith(params object[] steps) =>
        JsonSerializer.SerializeToElement(new { steps });

    private static object ValidStep() =>
        new { action = new { type = "send_message", message = "hi" } };

    private static CachedCommand StaleCommand(string graphJson) =>
        new()
        {
            Name = "shoutout",
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "pipeline",
            PipelineGraphJson = graphJson,
        };

    private static CachedChatTrigger StaleTrigger(string graphJson) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Pattern = "hello",
            MatchType = "contains",
            CaseSensitive = false,
            CooldownSeconds = 0,
            MinPermissionLevel = 0,
            PipelineGraphJson = graphJson,
        };

    /// <summary>
    /// Wires a registry substitute whose invalidation calls perform a REAL reload for the given channel's
    /// context — reading the current row straight out of <paramref name="db"/>, exactly like the production
    /// <c>ChannelRegistry</c> would, so the assertion proves actual cache behavior rather than a call count.
    /// </summary>
    private static IChannelRegistry BuildRegistry(
        AuthDbContext db,
        Guid broadcasterId,
        ChannelContext ctx
    )
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(broadcasterId).Returns(ctx);
        registry
            .InvalidateCommandsAsync(broadcasterId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                NomNomzBot.Domain.Commands.Entities.Pipeline? pipeline =
                    db.Pipelines.SingleOrDefault(p => p.BroadcasterId == broadcasterId);
                ctx.Commands["shoutout"] = StaleCommand(pipeline?.GraphJsonCache ?? "{}");
                return Task.CompletedTask;
            });
        registry
            .InvalidateChatTriggersAsync(broadcasterId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                NomNomzBot.Domain.Commands.Entities.Pipeline? pipeline =
                    db.Pipelines.SingleOrDefault(p => p.BroadcasterId == broadcasterId);
                ctx.ChatTriggers.Clear();
                CachedChatTrigger trigger = StaleTrigger(pipeline?.GraphJsonCache ?? "{}");
                ctx.ChatTriggers[trigger.Id] = trigger;
                return Task.CompletedTask;
            });
        return registry;
    }

    private static PipelineService BuildService(AuthDbContext db, IChannelRegistry registry) =>
        new(
            db,
            new PassThroughUnitOfWork(),
            Substitute.For<IEventBus>(),
            new CommandConfigValidator(
                [new FakeAction { ActionType = "send_message" }],
                new TemplateHelperValidator()
            ),
            registry
        );

    [Fact]
    public async Task UpdateAsync_invalidates_the_command_cache_so_the_hot_path_serves_the_new_graph()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChannelContext ctx = new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "twitch-a",
            ChannelName = "chan-a",
        };
        ctx.Commands["shoutout"] = StaleCommand("{\"steps\":[]}"); // pre-existing, stale entry
        IChannelRegistry registry = BuildRegistry(db, ChannelA, ctx);
        PipelineService service = BuildService(db, registry);

        Result<PipelineDto> created = await service.CreateAsync(
            ChannelA.ToString(),
            new() { Name = "shoutout-flow", GraphJsonCache = GraphWith(ValidStep()) }
        );
        created.IsSuccess.Should().BeTrue();

        Result<PipelineDto> updated = await service.UpdateAsync(
            ChannelA.ToString(),
            created.Value.Id,
            new() { GraphJsonCache = GraphWith(ValidStep(), ValidStep()) }
        );

        updated.IsSuccess.Should().BeTrue();

        // The lookup the chat pipeline actually performs (ChatMessageHandler: ctx.Commands.TryGetValue(...)) —
        // not an inspection of the cache's internals — now serves the freshly persisted two-step graph.
        JsonDocument
            .Parse(ctx.Commands["shoutout"].PipelineGraphJson!)
            .RootElement.GetProperty("steps")
            .GetArrayLength()
            .Should()
            .Be(2);

        await registry.Received(2).InvalidateCommandsAsync(ChannelA, Arg.Any<CancellationToken>()); // create + update
        await registry
            .Received(2)
            .InvalidateChatTriggersAsync(ChannelA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_invalidates_both_caches_so_a_deleted_pipeline_stops_being_served()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChannelContext ctx = new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "twitch-a",
            ChannelName = "chan-a",
        };
        IChannelRegistry registry = BuildRegistry(db, ChannelA, ctx);
        PipelineService service = BuildService(db, registry);

        Result<PipelineDto> created = await service.CreateAsync(
            ChannelA.ToString(),
            new() { Name = "temp-flow", GraphJsonCache = GraphWith(ValidStep()) }
        );
        ctx.Commands["shoutout"] = StaleCommand(created.Value.GraphJsonCache.ToString()!);

        Result deleted = await service.DeleteAsync(ChannelA.ToString(), created.Value.Id);

        deleted.IsSuccess.Should().BeTrue();

        // After the reload the pipeline row is gone, so the simulated reload falls back to the cache's "no
        // pipeline bound" shape — the deleted graph is no longer what the hot path would execute.
        ctx.Commands["shoutout"].PipelineGraphJson.Should().Be("{}");

        await registry.Received(2).InvalidateCommandsAsync(ChannelA, Arg.Any<CancellationToken>()); // create + delete
        await registry
            .Received(2)
            .InvalidateChatTriggersAsync(ChannelA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_invalidates_both_caches_for_the_owning_channel_only()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChannelContext ctx = new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "twitch-a",
            ChannelName = "chan-a",
        };
        IChannelRegistry registry = BuildRegistry(db, ChannelA, ctx);
        PipelineService service = BuildService(db, registry);

        Result<PipelineDto> result = await service.CreateAsync(
            ChannelA.ToString(),
            new() { Name = "new-flow", GraphJsonCache = GraphWith(ValidStep()) }
        );

        result.IsSuccess.Should().BeTrue();
        await registry.Received(1).InvalidateCommandsAsync(ChannelA, Arg.Any<CancellationToken>());
        await registry
            .Received(1)
            .InvalidateChatTriggersAsync(ChannelA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_never_touches_another_channels_cache()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        ChannelContext ctxA = new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "twitch-a",
            ChannelName = "chan-a",
        };
        ChannelContext ctxB = new()
        {
            BroadcasterId = ChannelB,
            TwitchChannelId = "twitch-b",
            ChannelName = "chan-b",
        };
        ctxB.Commands["untouched"] = StaleCommand("{\"marker\":\"channel-b-original\"}");

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(ChannelA).Returns(ctxA);
        registry.Get(ChannelB).Returns(ctxB);
        registry
            .InvalidateCommandsAsync(ChannelA, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ctxA.Commands["shoutout"] = StaleCommand("{\"marker\":\"channel-a-updated\"}");
                return Task.CompletedTask;
            });

        PipelineService service = BuildService(db, registry);
        Result<PipelineDto> created = await service.CreateAsync(
            ChannelA.ToString(),
            new() { Name = "a-flow", GraphJsonCache = GraphWith(ValidStep()) }
        );
        await service.UpdateAsync(
            ChannelA.ToString(),
            created.Value.Id,
            new() { GraphJsonCache = GraphWith(ValidStep(), ValidStep()) }
        );

        // Channel A's own cache changed...
        ctxA.Commands["shoutout"].PipelineGraphJson.Should().Contain("channel-a-updated");
        // ...but channel B's cache — a different tenant entirely — was never invalidated or mutated.
        await registry
            .DidNotReceive()
            .InvalidateCommandsAsync(ChannelB, Arg.Any<CancellationToken>());
        await registry
            .DidNotReceive()
            .InvalidateChatTriggersAsync(ChannelB, Arg.Any<CancellationToken>());
        ctxB.Commands["untouched"].PipelineGraphJson.Should().Contain("channel-b-original");
    }
}
