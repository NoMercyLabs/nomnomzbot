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
/// These tests hold the two things that make it actually work on stream: the raid call goes FIRST
/// (Twitch's 90s server timer starts the moment it returns, so a countdown built before it would be
/// counting down to nothing), and the announced countdown lands just before that fire moment rather
/// than finishing early into silence or overrunning it.
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
    public async Task The_raid_fires_first_and_the_ending_scene_comes_up_before_any_countdown()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        List<PipelineStep> steps = StepsOf(db);
        steps[0].ActionType.Should().Be("start_raid");
        steps[0].ConfigJson.Should().Contain("{args.1}");
        steps[1].ActionType.Should().Be("obs_switch_scene");

        // Nothing may wait or speak ahead of the raid call: Twitch's 90s window only starts once
        // start_raid returns, so a step before it pushes the whole countdown past the fire moment.
        steps
            .TakeWhile(s => s.ActionType != "start_raid")
            .Should()
            .BeEmpty("the raid call starts the clock everything else is timed against");
    }

    /// <summary>
    /// Confirmed live 2026-09-01: <c>!raid</c> executes through <c>ChatMessageHandler</c>'s FLAT
    /// <c>Command.PipelineGraphJson</c> graph (built by <c>PipelineGraphBuilder</c> from these very
    /// rows), never by <c>PipelineId</c> — so a <c>detached_step</c>/<c>try</c> block-kind wrapper is
    /// dead weight there: the flat reader has no concept of nested blocks, and a wrapper row just reads
    /// back as an ordinary step whose ActionType is the literal string "detached_step", which has no
    /// registered action and fails closed immediately (confirmed: this is EXACTLY what happened when an
    /// earlier version of this seeder used that wrapper — "Unknown action type 'detached_step'", right
    /// after start_raid, aborting everything after it). Every step here must be a plain top-level leaf;
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
    public async Task The_ending_scene_switch_continues_on_error_so_a_broken_OBS_never_blocks_the_raid()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        // Matches the legacy bot's fire-and-forget `_ = SwitchToEndingScene(...)` — an OBS hiccup here
        // must never take down the countdown, "RAID LIVE!", or stopping the stream/music (confirmed live
        // 2026-09-01: "OBS connection closed" on this ONE step killed the entire rest of the raid while
        // Twitch's clock kept ticking).
        StepsOf(db)
            .Single(s => s.ActionType == "obs_switch_scene")
            .ContinueOnError.Should()
            .BeTrue();
    }

    [Fact]
    public async Task Stopping_the_stream_and_pausing_the_music_each_continue_on_error_independently()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        // The legacy bot wraps StopStreaming and PauseSpotify in their OWN try/catch, each only logging a
        // warning on failure — one failing must never stop the other from running (confirmed live
        // 2026-09-01: without ContinueOnError on obs_streaming, a stop-streaming failure on a flaky OBS
        // bridge left music_pause never running at all).
        List<PipelineStep> steps = StepsOf(db);
        steps.Single(s => s.ActionType == "obs_streaming").ContinueOnError.Should().BeTrue();
        steps.Single(s => s.ActionType == "music_pause").ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public async Task The_countdown_lands_just_before_twitch_fires_the_raid_at_ninety_seconds()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        int totalWaitSeconds = StepsOf(db)
            .Where(s => s.ActionType == "wait")
            .Sum(s =>
                System
                    .Text.Json.JsonDocument.Parse(s.ConfigJson)
                    .RootElement.GetProperty("seconds")
                    .GetInt32()
            );

        // Twitch auto-fires at T+90s and cannot be committed early. Finishing early leaves viewers
        // watching silence; overrunning announces a raid that already happened.
        totalWaitSeconds.Should().BeInRange(80, 90);
    }

    [Fact]
    public async Task The_last_stretch_is_announced_and_the_early_waits_stay_silent()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        List<string> messages = StepsOf(db)
            .Where(s => s.ActionType == "send_message")
            .Select(s =>
                System
                    .Text.Json.JsonDocument.Parse(s.ConfigJson)
                    .RootElement.GetProperty("message")
                    .GetString()!
            )
            .ToList();

        messages.Should().Contain(m => m.Contains("RAID INCOMING"));
        foreach (int mark in new[] { 15, 10, 5, 3, 2 })
            messages.Should().Contain(m => m.Contains($"Raid in {mark} seconds"));
        messages.Should().Contain(m => m.Contains("Raid in 1 second..."));
        messages.Should().Contain(m => m.Contains("RAID LIVE"));

        // A "45 seconds left" line this early reads as spam — those waits are deliberately silent.
        messages.Should().NotContain(m => m.Contains("Raid in 45") || m.Contains("Raid in 30"));
    }

    /// <summary>
    /// Confirmed live 2026-09-01 (raid to jddoesdev): chat actually showed "RAID INCOMING to
    /// {jddoesdev}!" — a leftover decorative brace pair around the resolved name, from an earlier
    /// version of this seed line that wrapped the template engine's own single-brace {args.1} token in
    /// an extra escaped layer. Asserts the RAW, UNRESOLVED config text carries the token exactly once,
    /// with no brace left over on either side for the resolver to skip past.
    /// </summary>
    [Fact]
    public async Task The_raid_incoming_message_carries_the_args_token_with_no_leftover_braces()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        string raidIncoming = StepsOf(db)
            .Where(s => s.ActionType == "send_message")
            .Select(s =>
                System
                    .Text.Json.JsonDocument.Parse(s.ConfigJson)
                    .RootElement.GetProperty("message")
                    .GetString()!
            )
            .Single(m => m.Contains("RAID INCOMING"));

        raidIncoming.Should().Contain("RAID INCOMING to {args.1}!");
        raidIncoming.Should().NotContain("{{args.1}}");
        raidIncoming.Should().NotContain("{{{args.1}}}");
    }

    [Fact]
    public async Task The_stream_stops_and_the_music_pauses_only_after_the_raid_goes_live()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        List<PipelineStep> steps = StepsOf(db);
        int liveIndex = steps.FindIndex(s =>
            s.ActionType == "send_message" && s.ConfigJson.Contains("RAID LIVE")
        );
        int stopIndex = steps.FindIndex(s => s.ActionType == "obs_streaming");
        int pauseIndex = steps.FindIndex(s => s.ActionType == "music_pause");

        liveIndex.Should().BeGreaterThan(-1);
        // Stopping the stream before the audience has actually moved strands them on an offline channel.
        stopIndex.Should().BeGreaterThan(liveIndex);
        pauseIndex.Should().BeGreaterThan(liveIndex);
        steps[stopIndex].ConfigJson.Should().Contain("stop");
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
