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
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.PlatformContent;
using Timer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Spec commands-pipelines 12.6: a surface row bound to an untouched one-step wrapper pipeline owned by exactly
/// one command of its channel runs that command directly. Customized, multi-step, referenced or ownerless
/// pipelines stay bound as pipelines. A pipeline row is never deleted and a second pass changes nothing.
/// </summary>
public sealed class WrapperPipelineMigratorTests
{
    private const string OneStepGraph = """{"steps":[{"id":"s1","type":"send_message"}]}""";
    private const string TwoStepGraph =
        """{"steps":[{"id":"s1","type":"send_message"},{"id":"s2","type":"wait"}]}""";

    private static async Task<(Pipeline Pipeline, Command Command)> AddWrapperAsync(
        RealCommandsDb h,
        Guid channel,
        string name,
        string graph = OneStepGraph,
        bool stamped = true
    )
    {
        Pipeline pipeline = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = name,
            GraphJsonCache = graph,
            PlatformSourceHash = stamped ? PlatformContentHash.ComputeHash(graph) : "stale-hash",
        };
        Command command = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = name,
            NameNormalized = name,
            Tier = "pipeline",
            PipelineId = pipeline.Id,
        };
        h.Db.Pipelines.Add(pipeline);
        h.Db.Commands.Add(command);
        await h.Db.SaveChangesAsync();
        return (pipeline, command);
    }

    private static async Task<Reward> AddRewardAsync(RealCommandsDb h, Guid channel, Guid pipeline)
    {
        Reward reward = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Title = "Hydrate",
            PipelineId = pipeline,
        };
        h.Db.Rewards.Add(reward);
        await h.Db.SaveChangesAsync();
        return reward;
    }

    private static WrapperPipelineMigrator Sut(RealCommandsDb h) => new(h.Db);

    private static async Task<T> ReloadAsync<T>(RealCommandsDb h, T row)
        where T : class
    {
        await h.Db.Entry(row).ReloadAsync();
        return row;
    }

    [Fact]
    public async Task A_reward_bound_to_a_wrapper_pipeline_runs_the_command_directly()
    {
        using RealCommandsDb h = new();
        (Pipeline pipeline, Command command) = await AddWrapperAsync(
            h,
            RealCommandsDb.Channel,
            "hydrate"
        );
        Reward reward = await AddRewardAsync(h, RealCommandsDb.Channel, pipeline.Id);

        WrapperMigrationReport report = await Sut(h).RunAsync();

        await ReloadAsync(h, reward);
        reward.TargetKind.Should().Be(BindingTargetKind.Command);
        reward.TargetCommandId.Should().Be(command.Id);
        reward.PipelineId.Should().BeNull();
        report
            .Rebound.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new ReboundBinding("Rewards", reward.Id, pipeline.Id, command.Id));
        report.Kept.Should().BeEmpty();
        (await h.Db.Pipelines.AnyAsync(p => p.Id == pipeline.Id))
            .Should()
            .BeTrue("a pipeline row is never deleted");
    }

    [Fact]
    public async Task Every_surface_bound_to_a_wrapper_pipeline_is_rebound()
    {
        using RealCommandsDb h = new();
        Guid channel = RealCommandsDb.Channel;
        (Pipeline pipeline, Command command) = await AddWrapperAsync(h, channel, "hydrate");
        Timer timer = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "t",
            PipelineId = pipeline.Id,
        };
        EventResponse response = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            EventType = "follow",
            PipelineId = pipeline.Id,
        };
        ChatTrigger trigger = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Pattern = "gg",
            PipelineId = pipeline.Id,
        };
        ScheduledPipelineTask task = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            PipelineId = pipeline.Id,
        };
        Giveaway giveaway = new()
        {
            BroadcasterId = channel,
            Title = "g",
            EntryMode = "keyword",
            PrizeMode = "none",
            PrizePipelineId = pipeline.Id,
        };
        CatalogItem item = new()
        {
            BroadcasterId = channel,
            Name = "Item",
            NameNormalized = "item",
            SinkType = "pipeline",
            PipelineId = pipeline.Id,
        };
        Reward reward = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Title = "r",
            PipelineId = pipeline.Id,
        };
        h.Db.Timers.Add(timer);
        h.Db.EventResponses.Add(response);
        h.Db.ChatTriggers.Add(trigger);
        h.Db.ScheduledPipelineTasks.Add(task);
        h.Db.Giveaways.Add(giveaway);
        h.Db.CatalogItems.Add(item);
        h.Db.Rewards.Add(reward);
        await h.Db.SaveChangesAsync();

        WrapperMigrationReport report = await Sut(h).RunAsync();

        report
            .Rebound.Select(r => r.Surface)
            .Should()
            .BeEquivalentTo(
                "Rewards",
                "Timers",
                "EventResponses",
                "ChatTriggers",
                "ScheduledPipelineTasks",
                "Giveaways",
                "CatalogItems"
            );
        foreach (
            IBindingTargetHolder holder in new IBindingTargetHolder[]
            {
                await ReloadAsync(h, reward),
                await ReloadAsync(h, timer),
                await ReloadAsync(h, response),
                await ReloadAsync(h, trigger),
                await ReloadAsync(h, task),
                await ReloadAsync(h, giveaway),
                await ReloadAsync(h, item),
            }
        )
        {
            holder.TargetKind.Should().Be(BindingTargetKind.Command);
            holder.TargetCommandId.Should().Be(command.Id);
            holder.LegacyPipelineId.Should().BeNull();
        }
    }

    [Fact]
    public async Task A_customized_wrapper_stays_a_pipeline_binding()
    {
        using RealCommandsDb h = new();
        (Pipeline pipeline, _) = await AddWrapperAsync(
            h,
            RealCommandsDb.Channel,
            "hydrate",
            stamped: false
        );
        Reward reward = await AddRewardAsync(h, RealCommandsDb.Channel, pipeline.Id);

        WrapperMigrationReport report = await Sut(h).RunAsync();

        await ReloadAsync(h, reward);
        reward.TargetKind.Should().Be(BindingTargetKind.Pipeline);
        reward.PipelineId.Should().Be(pipeline.Id);
        report.Rebound.Should().BeEmpty();
        report.Kept.Should().ContainSingle().Which.PipelineId.Should().Be(pipeline.Id);
    }

    [Fact]
    public async Task A_multi_step_pipeline_stays_a_pipeline_binding()
    {
        using RealCommandsDb h = new();
        (Pipeline pipeline, _) = await AddWrapperAsync(
            h,
            RealCommandsDb.Channel,
            "hydrate",
            TwoStepGraph
        );
        Reward reward = await AddRewardAsync(h, RealCommandsDb.Channel, pipeline.Id);

        WrapperMigrationReport report = await Sut(h).RunAsync();

        await ReloadAsync(h, reward);
        reward.TargetKind.Should().Be(BindingTargetKind.Pipeline);
        report.Rebound.Should().BeEmpty();
        report.Kept.Should().ContainSingle().Which.PipelineId.Should().Be(pipeline.Id);
    }

    [Fact]
    public async Task A_pipeline_with_its_own_trigger_or_a_step_that_names_it_stays()
    {
        using RealCommandsDb h = new();
        Guid channel = RealCommandsDb.Channel;
        (Pipeline withTrigger, _) = await AddWrapperAsync(h, channel, "triggered");
        (Pipeline named, _) = await AddWrapperAsync(h, channel, "named");
        (Pipeline caller, _) = await AddWrapperAsync(h, channel, "caller", TwoStepGraph);
        h.Db.PipelineTriggers.Add(
            new()
            {
                Id = Guid.NewGuid(),
                PipelineId = withTrigger.Id,
                BroadcasterId = channel,
                Kind = "manual",
            }
        );
        h.Db.PipelineSteps.Add(
            new()
            {
                Id = Guid.NewGuid(),
                PipelineId = caller.Id,
                BroadcasterId = channel,
                Order = 0,
                ActionType = "run_pipeline",
                ConfigJson = $$"""{"pipeline_id":"{{named.Id}}"}""",
            }
        );
        Reward first = await AddRewardAsync(h, channel, withTrigger.Id);
        Reward second = await AddRewardAsync(h, channel, named.Id);

        WrapperMigrationReport report = await Sut(h).RunAsync();

        await ReloadAsync(h, first);
        await ReloadAsync(h, second);
        first.TargetKind.Should().Be(BindingTargetKind.Pipeline);
        second.TargetKind.Should().Be(BindingTargetKind.Pipeline);
        report.Rebound.Should().BeEmpty();
        report.Kept.Select(k => k.PipelineId).Should().BeEquivalentTo([withTrigger.Id, named.Id]);
    }

    [Fact]
    public async Task A_pipeline_with_no_owning_command_or_two_stays()
    {
        using RealCommandsDb h = new();
        Guid channel = RealCommandsDb.Channel;
        (Pipeline orphan, Command orphanCommand) = await AddWrapperAsync(h, channel, "orphan");
        (Pipeline shared, _) = await AddWrapperAsync(h, channel, "shared");
        h.Db.Commands.Remove(orphanCommand);
        h.Db.Commands.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "shared2",
                NameNormalized = "shared2",
                Tier = "pipeline",
                PipelineId = shared.Id,
            }
        );
        await h.Db.SaveChangesAsync();
        Reward first = await AddRewardAsync(h, channel, orphan.Id);
        Reward second = await AddRewardAsync(h, channel, shared.Id);

        WrapperMigrationReport report = await Sut(h).RunAsync();

        await ReloadAsync(h, first);
        await ReloadAsync(h, second);
        first.TargetKind.Should().Be(BindingTargetKind.Pipeline);
        second.TargetKind.Should().Be(BindingTargetKind.Pipeline);
        report.Rebound.Should().BeEmpty();
        report.Kept.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_second_run_changes_nothing_and_reports_nothing()
    {
        using RealCommandsDb h = new();
        (Pipeline pipeline, Command command) = await AddWrapperAsync(
            h,
            RealCommandsDb.Channel,
            "hydrate"
        );
        Reward reward = await AddRewardAsync(h, RealCommandsDb.Channel, pipeline.Id);

        WrapperMigrationReport first = await Sut(h).RunAsync();
        WrapperMigrationReport second = await Sut(h).RunAsync();

        first.Rebound.Should().HaveCount(1);
        second.Rebound.Should().BeEmpty();
        second.Kept.Should().BeEmpty();
        await ReloadAsync(h, reward);
        reward.TargetCommandId.Should().Be(command.Id);
    }

    [Fact]
    public async Task Each_channel_rebinds_to_its_own_command_and_never_to_another_tenants()
    {
        using RealCommandsDb h = new();
        Guid other = Guid.Parse("0192a000-0000-7000-8000-00000000c502");
        Guid otherOwner = Guid.Parse("0192a000-0000-7000-8000-00000000c503");
        h.Db.Users.Add(
            new()
            {
                Id = otherOwner,
                TwitchUserId = "tw-owner-c5b",
                Username = "other",
                UsernameNormalized = "other",
                DisplayName = "Other",
            }
        );
        h.Db.Channels.Add(
            new()
            {
                Id = other,
                OwnerUserId = otherOwner,
                TwitchChannelId = "tw-channel-c5b",
                ExternalChannelId = "tw-channel-c5b",
                Name = "other",
                NameNormalized = "other",
            }
        );
        await h.Db.SaveChangesAsync();
        (Pipeline minePipeline, Command mine) = await AddWrapperAsync(
            h,
            RealCommandsDb.Channel,
            "hydrate"
        );
        (Pipeline theirPipeline, Command theirs) = await AddWrapperAsync(h, other, "hydrate");
        Reward mineReward = await AddRewardAsync(h, RealCommandsDb.Channel, minePipeline.Id);
        Reward theirReward = await AddRewardAsync(h, other, theirPipeline.Id);
        Reward crossed = await AddRewardAsync(h, other, minePipeline.Id);

        WrapperMigrationReport report = await Sut(h).RunAsync();

        await ReloadAsync(h, mineReward);
        await ReloadAsync(h, theirReward);
        await ReloadAsync(h, crossed);
        mineReward.TargetCommandId.Should().Be(mine.Id);
        theirReward.TargetCommandId.Should().Be(theirs.Id);
        crossed.TargetKind.Should().Be(BindingTargetKind.Pipeline);
        crossed.PipelineId.Should().Be(minePipeline.Id);
        report.Rebound.Should().HaveCount(2);
    }
}
