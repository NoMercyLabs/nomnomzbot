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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Tests.Content;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// The <c>!raid</c> raid-out flow ships as an ordinary editable pipeline made only of generic blocks.
/// It deliberately carries NO countdown or fixed-wait timing (see <see cref="RaidCommitFlowSeeder"/> for
/// where stopping the stream/pausing music/confirming the raid now live, reactively) — these tests hold
/// only that the raid call goes first and the flow reads back as a plain, flat leaf sequence.
/// </summary>
public sealed class RaidFlowSeedingTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4b00-3333-7000-8000-000000000001");

    private static (RaidFlowSeeder Seeder, SeedTestDbContext Db) Build()
    {
        SeedTestDbContext db = SeedTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Tenant,
                Name = "raid-test-channel",
                NameNormalized = "raid-test-channel",
            }
        );
        db.SaveChanges();
        return (new RaidFlowSeeder(db), db);
    }

    private static List<PipelineStep> StepsOf(SeedTestDbContext db) =>
        db.PipelineSteps.OrderBy(s => s.Order).ToList();

    [Fact]
    public async Task A_channel_gets_a_broadcaster_only_raid_command_backed_by_a_pipeline()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        Command command = db.Commands.Single(c => c.NameNormalized == "raid");
        command.Tier.Should().Be("pipeline");
        command.IsEnabled.Should().BeTrue();
        command.PipelineId.Should().NotBeNull();
        // Raiding hands the whole audience away and ends the stream — never a moderator's call.
        command.MinPermissionLevel.Should().Be(PermissionLevel.Broadcaster.ToLevelValue());

        db.Pipelines.Single(p => p.Id == command.PipelineId).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task The_command_starts_the_raid_and_does_nothing_else()
    {
        // The scene switch and the chat call-out used to live here, firing the moment the command ran —
        // before Twitch had validated anything. A raid it rejected (target offline, bad name, no
        // permission) still switched the scene and posted to chat. They now hang off channel.raid.start,
        // which Twitch only sends once the raid is real.
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        List<PipelineStep> steps = StepsOf(db);
        steps.Should().ContainSingle();
        steps[0].ActionType.Should().Be("start_raid");
        steps[0].ConfigJson.Should().Contain("{args.1}");
    }

    [Fact]
    public async Task The_command_never_switches_scenes_or_talks_to_chat_itself()
    {
        // Named separately from the count above: a future step appended here would still leave that one
        // green if it also updated the count, and this is the property that actually matters — the
        // command commits to nothing that a rejected raid would leave stranded.
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        List<PipelineStep> steps = StepsOf(db);
        steps.Should().NotContain(s => s.ActionType == "obs_switch_scene");
        steps.Should().NotContain(s => s.ActionType == "send_message");
        steps.Should().NotContain(s => s.ActionType == "wait");
    }

    /// <summary>
    /// Confirmed live 2026-09-01: <c>!raid</c> executes through <c>ChatMessageHandler</c>'s FLAT
    /// <c>Command.PipelineGraphJson</c> graph (built by <c>PipelineGraphBuilder</c> from these very
    /// rows), never by <c>PipelineId</c> — so a <c>detached_step</c>/<c>try</c> block-kind wrapper is
    /// dead weight there: the flat reader has no concept of nested blocks, and a wrapper row just reads
    /// back as an ordinary step whose ActionType is the literal string "detached_step", which has no
    /// registered action and fails closed immediately. Every step here must be a plain top-level leaf;
    /// only <see cref="PipelineStep.ContinueOnError"/> is honored by the flat runtime.
    /// </summary>
    [Fact]
    public async Task Every_step_is_a_plain_top_level_leaf_never_a_block_kind_wrapper()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        StepsOf(db)
            .Should()
            .OnlyContain(
                s => s.ParentStepId == null && s.BlockKind == null,
                "the flat graph the hot chat path actually executes has no concept of nested blocks"
            );
    }

    [Fact]
    public async Task The_pipeline_carries_no_deadline_synced_wait_and_never_stops_the_stream_itself()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        // The whole point of moving to a reactive channel.raid.out flow: nothing here guesses an
        // elapsed-time offset. Three live recalibrations of that guess (90s -> 103s -> 116s) each came
        // back reported wrong by the same margin — a genuine architectural dead end, not a tuning gap.
        // Plain, un-synced 1s pacing waits between the intro messages are fine and still present.
        List<PipelineStep> steps = StepsOf(db);
        steps.Should().NotContain(s => s.ActionType == "wait_until_raid_fires");
        steps
            .Should()
            .NotContain(s => s.ActionType == "obs_streaming" || s.ActionType == "music_pause");
    }

    /// <summary>
    /// The failure this actually hit on the owner's channel: he already had a <c>raid</c> command whose
    /// pipeline was EMPTY — a stub he had created and never filled in. Skipping purely on "a command with
    /// this name exists" meant the flow was never installed, so <c>!raid</c> ran an empty pipeline and did
    /// nothing at all, silently, while the seeder reported itself satisfied.
    /// <para>
    /// A command whose pipeline has no steps is not "already seeded", it is a stub — so the flow fills it in.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_existing_raid_command_with_an_empty_pipeline_gets_the_flow_filled_in()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();
        Pipeline stub = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Tenant,
            Name = "Raid",
            TriggerKind = "command",
            IsEnabled = true,
        };
        db.Pipelines.Add(stub);
        db.Commands.Add(
            new Command
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                Name = "raid",
                NameNormalized = "raid",
                Tier = "pipeline",
                PipelineId = stub.Id,
                IsEnabled = true,
            }
        );
        db.SaveChanges();

        await seeder.SeedAsync(Tenant);

        db.Commands.Count(c => c.NameNormalized == "raid")
            .Should()
            .Be(1, "the streamer's own command row is kept, not duplicated");
        List<PipelineStep> steps = StepsOf(db);
        steps.Should().NotBeEmpty("an empty pipeline is a stub, not a finished flow");
        steps[0].ActionType.Should().Be("start_raid");
        steps
            .Should()
            .OnlyContain(
                step => step.PipelineId == stub.Id,
                "the flow fills in the pipeline the command already points at"
            );
    }

    /// <summary>A pipeline the streamer HAS built is never touched, however few steps it has.</summary>
    [Fact]
    public async Task An_existing_raid_pipeline_that_has_steps_is_left_completely_alone()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();
        Pipeline mine = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Tenant,
            Name = "My raid",
            TriggerKind = "command",
            IsEnabled = true,
        };
        db.Pipelines.Add(mine);
        db.PipelineSteps.Add(
            new PipelineStep
            {
                Id = Guid.CreateVersion7(),
                PipelineId = mine.Id,
                BroadcasterId = Tenant,
                ActionType = "send_message",
                ConfigJson = """{"message":"bye"}""",
                Order = 0,
                IsEnabled = true,
            }
        );
        db.Commands.Add(
            new Command
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                Name = "raid",
                NameNormalized = "raid",
                Tier = "pipeline",
                PipelineId = mine.Id,
                IsEnabled = true,
            }
        );
        db.SaveChanges();

        await seeder.SeedAsync(Tenant);

        StepsOf(db).Should().HaveCount(1, "the streamer's own flow is never rewritten");
        StepsOf(db)[0].ActionType.Should().Be("send_message");
    }

    [Fact]
    public async Task Seeding_twice_never_duplicates_and_never_overwrites_the_streamers_own_edits()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();
        await seeder.SeedAsync(Tenant);

        Command seeded = db.Commands.Single(c => c.NameNormalized == "raid");
        seeded.Description = "my own wording";
        db.SaveChanges();
        int stepCountAfterFirstSeed = db.PipelineSteps.Count();

        await seeder.SeedAsync(Tenant);

        db.Commands.Count(c => c.NameNormalized == "raid").Should().Be(1);
        db.PipelineSteps.Count().Should().Be(stepCountAfterFirstSeed);
        db.Commands.Single(c => c.NameNormalized == "raid")
            .Description.Should()
            .Be("my own wording", "a re-seed must never clobber what the streamer changed");
    }
}
