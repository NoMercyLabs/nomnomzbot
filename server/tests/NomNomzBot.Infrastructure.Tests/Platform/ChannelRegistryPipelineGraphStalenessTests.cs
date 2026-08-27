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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// Proves the fix for the live "!so requires a non-empty user_id" incident (2026-08-27): a command's
/// <see cref="Pipeline.GraphJsonCache"/> is only a build-time performance cache — the normalized
/// <see cref="PipelineStep"/> rows are the execution truth. A cache written before the args-1-based-index
/// fix shipped can drift arbitrarily far from what the live editor shows and the tree model stores; the
/// chat hot path must never trust a stale cache when live step rows exist for the same pipeline.
/// </summary>
public sealed class ChannelRegistryPipelineGraphStalenessTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f5001");
    private static readonly Guid PipelineId = Guid.Parse("0192a000-0000-7000-8000-0000000f5002");

    private static string DatabaseName => Guid.NewGuid().ToString();

    [Fact]
    public async Task A_command_with_live_steps_ignores_a_stale_graph_cache_and_uses_the_steps_instead()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(DatabaseName);
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                Name = "testchannel",
                NameNormalized = "testchannel",
                TwitchChannelId = "123456",
                CreatedAt = DateTime.UtcNow,
            }
        );
        db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = ChannelId,
                Name = "so",
                TriggerKind = "command",
                IsEnabled = true,
                // The stale cache: written before the 1-based args fix, still says {args.0}.
                GraphJsonCache =
                    """{"steps":[{"action":{"type":"shoutout","user_id":"{args.0}"}}]}""",
            }
        );
        db.PipelineSteps.Add(
            new()
            {
                Id = Guid.NewGuid(),
                PipelineId = PipelineId,
                BroadcasterId = ChannelId,
                Order = 0,
                ActionType = "shoutout",
                // The live, correct tree-model row: {args.1}.
                ConfigJson = """{"type":"shoutout","user_id":"{args.1}"}""",
            }
        );
        db.Commands.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = ChannelId,
                Name = "so",
                NameNormalized = "so",
                Tier = "pipeline",
                PipelineId = PipelineId,
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();

        ChannelRegistry registry = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChannelRegistry>.Instance,
            TimeProvider.System
        );

        ChannelContext ctx = await registry.GetOrCreateAsync(ChannelId, "123456", "testchannel");

        ctx.Commands.Should().ContainKey("so");
        ctx.Commands["so"].PipelineGraphJson.Should().NotBeNull();
        ctx.Commands["so"].PipelineGraphJson.Should().Contain("args.1");
        ctx.Commands["so"].PipelineGraphJson.Should().NotContain("args.0");
    }

    [Fact]
    public async Task A_command_with_no_split_out_steps_still_falls_back_to_the_graph_cache()
    {
        Guid legacyPipelineId = Guid.Parse("0192a000-0000-7000-8000-0000000f5003");
        AuthDbContext db = AuthTestBuilder.NewContext(DatabaseName);
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                Name = "testchannel",
                NameNormalized = "testchannel",
                TwitchChannelId = "123456",
                CreatedAt = DateTime.UtcNow,
            }
        );
        db.Pipelines.Add(
            new()
            {
                Id = legacyPipelineId,
                BroadcasterId = ChannelId,
                Name = "legacy",
                TriggerKind = "command",
                IsEnabled = true,
                GraphJsonCache =
                    """{"steps":[{"action":{"type":"send_message","message":"hi"}}]}""",
            }
        );
        db.Commands.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = ChannelId,
                Name = "legacy",
                NameNormalized = "legacy",
                Tier = "pipeline",
                PipelineId = legacyPipelineId,
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();

        ChannelRegistry registry = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChannelRegistry>.Instance,
            TimeProvider.System
        );

        ChannelContext ctx = await registry.GetOrCreateAsync(ChannelId, "123456", "testchannel");

        ctx.Commands["legacy"].PipelineGraphJson.Should().Contain("send_message");
    }
}
