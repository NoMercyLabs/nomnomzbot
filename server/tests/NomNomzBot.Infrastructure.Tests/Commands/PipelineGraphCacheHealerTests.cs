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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Content.PlatformContent;
using NomNomzBot.Infrastructure.Tests.Content;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// The timer service and the reward handler read only <see cref="Pipeline.GraphJsonCache"/>. A pipeline
/// saved with step rows and no cache therefore ran nothing when a timer or reward fired. The healer fills
/// the cache from the rows, at startup and after the writers that save rows alone.
/// </summary>
public sealed class PipelineGraphCacheHealerTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4d00-5555-7000-8000-000000000001");

    private static SeedTestDbContext BuildDb()
    {
        SeedTestDbContext db = SeedTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Tenant,
                Name = "heal-test-channel",
                NameNormalized = "heal-test-channel",
            }
        );
        db.SaveChanges();
        return db;
    }

    private static Pipeline AddRowsOnlyPipeline(SeedTestDbContext db, string triggerKind)
    {
        Pipeline pipeline = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Tenant,
            Name = triggerKind + " flow",
            TriggerKind = triggerKind,
            IsEnabled = true,
        };
        db.Pipelines.Add(pipeline);
        string[] actions = ["send_message", "run_code"];
        for (int i = 0; i < actions.Length; i++)
        {
            db.PipelineSteps.Add(
                new()
                {
                    Id = Guid.CreateVersion7(),
                    PipelineId = pipeline.Id,
                    BroadcasterId = Tenant,
                    ActionType = actions[i],
                    ConfigJson = "{}",
                    Order = i,
                    IsEnabled = true,
                }
            );
        }
        db.SaveChanges();
        return pipeline;
    }

    // The exact projection TimerService.cs and RewardRedeemedHandler.cs run before they execute anything.
    private static async Task<string?> CacheAsConsumersReadItAsync(SeedTestDbContext db, Guid id) =>
        await db
            .Pipelines.Where(p => p.Id == id && p.BroadcasterId == Tenant && p.DeletedAt == null)
            .Select(p => p.GraphJsonCache)
            .FirstOrDefaultAsync();

    [Theory]
    [InlineData("timer")]
    [InlineData("reward")]
    public async Task A_timer_or_reward_pipeline_with_rows_and_no_cache_has_a_graph_after_the_heal(
        string triggerKind
    )
    {
        SeedTestDbContext db = BuildDb();
        Pipeline pipeline = AddRowsOnlyPipeline(db, triggerKind);
        (await CacheAsConsumersReadItAsync(db, pipeline.Id))
            .Should()
            .BeNull("the fixture is a rows-only pipeline");

        await new PipelineGraphCacheHealSeeder(new PipelineGraphCacheHealer(db)).SeedAsync();

        string? graph = await CacheAsConsumersReadItAsync(db, pipeline.Id);
        graph.Should().NotBeNullOrEmpty();
        graph
            .IndexOf("send_message", StringComparison.Ordinal)
            .Should()
            .BeLessThan(
                graph.IndexOf("run_code", StringComparison.Ordinal),
                "the graph keeps the step order"
            );
    }

    [Fact]
    public async Task A_filled_cache_and_a_pipeline_without_rows_are_left_alone()
    {
        SeedTestDbContext db = BuildDb();
        Pipeline filled = AddRowsOnlyPipeline(db, "command");
        filled.GraphJsonCache = "{\"steps\":[]}";
        Pipeline empty = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Tenant,
            Name = "no rows",
            TriggerKind = "command",
            IsEnabled = true,
        };
        db.Pipelines.Add(empty);
        db.SaveChanges();

        int healed = await new PipelineGraphCacheHealer(db).HealMissingAsync();

        healed.Should().Be(0);
        db.Pipelines.Single(p => p.Id == filled.Id).GraphJsonCache.Should().Be("{\"steps\":[]}");
        db.Pipelines.Single(p => p.Id == empty.Id).GraphJsonCache.Should().BeNull();
    }

    [Fact]
    public async Task The_heal_restamps_the_platform_hash_so_the_row_is_not_customized()
    {
        SeedTestDbContext db = BuildDb();
        Pipeline pipeline = AddRowsOnlyPipeline(db, "command");
        pipeline.PlatformSourceHash = PlatformContentHash.ComputeHash(null);
        db.SaveChanges();

        await new PipelineGraphCacheHealer(db).HealMissingAsync();

        Pipeline stored = db.Pipelines.Single();
        stored.GraphJsonCache.Should().NotBeNullOrEmpty();
        stored
            .PlatformSourceHash.Should()
            .Be(
                PlatformContentHash.ComputeHash(stored.GraphJsonCache),
                "the stamp must equal the hash of the live cache, or the row reads as edited"
            );
    }

    [Fact]
    public async Task A_row_whose_hash_was_already_customized_keeps_its_own_hash()
    {
        SeedTestDbContext db = BuildDb();
        Pipeline pipeline = AddRowsOnlyPipeline(db, "command");
        pipeline.PlatformSourceHash = "stamped-from-something-else";
        db.SaveChanges();

        await new PipelineGraphCacheHealer(db).HealMissingAsync();

        db.Pipelines.Single().PlatformSourceHash.Should().Be("stamped-from-something-else");
    }
}
