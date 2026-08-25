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
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        SeedTestDbContext db = new(
            new DbContextOptionsBuilder<SeedTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options
        );
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
