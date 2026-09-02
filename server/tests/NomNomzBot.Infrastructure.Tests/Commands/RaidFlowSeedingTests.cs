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
        // The scene switch sits inside a detached_step wrapper (see the dedicated detachment test below),
        // so it is the SECOND top-level step even though its actual "obs_switch_scene" leaf is nested one
        // level deeper.
        steps[1].BlockKind.Should().Be("detached_step");

        // Nothing may wait or speak ahead of the raid call: Twitch's 90s window only starts once
        // start_raid returns, so a step before it pushes the whole countdown past the fire moment.
        steps
            .TakeWhile(s => s.ActionType != "start_raid")
            .Should()
            .BeEmpty("the raid call starts the clock everything else is timed against");
    }

    [Fact]
    public async Task The_ending_scene_switch_is_detached_so_a_slow_or_broken_OBS_never_blocks_the_raid()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        // pipeline-tree-and-editor.md §1.1/§3.1 item #4, matching the legacy bot's fire-and-forget
        // `_ = SwitchToEndingScene(...)` — the scene switch must be wrapped in a detached_step block so
        // its own failure or a stuck OBS connection can never stall or abort the countdown, "RAID LIVE!",
        // stopping the stream, or pausing the music (confirmed live 2026-09-01: an "OBS connection closed"
        // failure on this step used to fail the WHOLE pipeline closed, silently dropping every step after it).
        List<PipelineStep> steps = StepsOf(db);
        PipelineStep obsStep = steps.Single(s => s.ActionType == "obs_switch_scene");
        obsStep
            .ParentStepId.Should()
            .NotBeNull("it must live inside a detached_step wrapper, not top-level");

        PipelineStep wrapper = steps.Single(s => s.Id == obsStep.ParentStepId);
        wrapper.BlockKind.Should().Be("detached_step");

        // Every step downstream of the raid call — the countdown, "RAID LIVE!" — must sit as an ordinary
        // TOP-LEVEL sibling, never nested under the detached wrapper, so the engine keeps walking them
        // regardless of what the detached OBS action does. Only stop-streaming and pause-music are ALSO
        // nested (each under its own catch-less try — see the dedicated try-wrapping test below).
        HashSet<Guid> detachedOrTryIds = [obsStep.Id, wrapper.Id];
        steps
            .Where(s => !detachedOrTryIds.Contains(s.Id))
            .Where(s =>
                s.ActionType is not ("obs_streaming" or "music_pause") && s.BlockKind != "try"
            )
            .Should()
            .OnlyContain(
                s => s.ParentStepId == null,
                "only the OBS scene switch, stream-stop and music-pause are wrapped"
            );
    }

    [Fact]
    public async Task Stopping_the_stream_and_pausing_the_music_are_each_wrapped_so_one_failing_never_blocks_the_other()
    {
        (RaidFlowSeeder seeder, SeedTestDbContext db) = Build();

        await seeder.SeedAsync(Tenant);

        // The legacy bot wraps StopStreaming and PauseSpotify in their OWN try/catch, each only logging a
        // warning on failure — one failing must never stop the other from running. A catch-less `try`
        // block is the pipeline engine's match: swallow the failure, continue past the block (confirmed
        // live 2026-09-01: without this, a stop-streaming failure on a flaky OBS bridge left music_pause
        // never running at all).
        List<PipelineStep> steps = StepsOf(db);
        foreach (string actionType in new[] { "obs_streaming", "music_pause" })
        {
            PipelineStep leaf = steps.Single(s => s.ActionType == actionType);
            leaf.ParentStepId.Should()
                .NotBeNull($"{actionType} must live inside its own try wrapper");
            PipelineStep wrapper = steps.Single(s => s.Id == leaf.ParentStepId);
            wrapper.BlockKind.Should().Be("try");
            leaf.Branch.Should()
                .Be("then", "the leaf is the try's body, never its (absent) catch arm");
        }

        // Two SEPARATE wrappers, not one try holding both — a try's own catch swallows only ITS body's
        // failure, so sharing one wrapper would still let a failed stop-streaming skip music-pause.
        Guid streamingWrapperId = steps
            .Single(s => s.ActionType == "obs_streaming")
            .ParentStepId!.Value;
        Guid musicWrapperId = steps.Single(s => s.ActionType == "music_pause").ParentStepId!.Value;
        streamingWrapperId.Should().NotBe(musicWrapperId);
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
