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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Tests.Content;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// A new channel's raid response does what the old bot did for an incoming raid: the chat welcome, the
/// spoken line, and a priority shoutout of the raider. The platform default row carries a single chat
/// line and no steps, so the three steps are wired onto the channel's own untouched stub.
/// </summary>
public sealed class RaidResponseFlowSeederTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4b00-6666-7000-8000-000000000001");
    private const string EventType = "channel.raid";
    private const string LegacyLine = "{user} just raided with {viewers} viewers! Welcome raiders!";

    private static (RaidResponseFlowSeeder Seeder, SeedTestDbContext Db) Build()
    {
        SeedTestDbContext db = SeedTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Tenant,
                Name = "raid-response-test-channel",
                NameNormalized = "raid-response-test-channel",
            }
        );
        db.SaveChanges();
        return (new RaidResponseFlowSeeder(db), db);
    }

    private static EventResponse FreshStub() =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Tenant,
            EventType = EventType,
            ResponseType = "chat_message",
            Message = null,
            IsEnabled = false,
            FollowsPlatformDefault = true,
        };

    private static List<PipelineStep> StepsOf(SeedTestDbContext db)
    {
        Guid? pipelineId = db.EventResponses.Single(r => r.EventType == EventType).PipelineId;
        return db
            .PipelineSteps.Where(s => s.PipelineId == pipelineId)
            .OrderBy(s => s.Order)
            .ToList();
    }

    private static string Prop(PipelineStep step, string name) =>
        JsonDocument.Parse(step.ConfigJson).RootElement.GetProperty(name).GetString()!;

    [Fact]
    public async Task A_fresh_channels_raid_response_welcomes_speaks_and_shouts_out_in_that_order()
    {
        (RaidResponseFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(FreshStub());
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        EventResponse response = db.EventResponses.Single(r => r.EventType == EventType);
        response.ResponseType.Should().Be("pipeline");
        response.IsEnabled.Should().BeTrue();
        response
            .FollowsPlatformDefault.Should()
            .BeFalse("a pipeline only runs for a row that stopped following the platform default");

        List<PipelineStep> steps = StepsOf(db);
        steps.Select(s => s.ActionType).Should().Equal("send_message", "play_tts", "shoutout");
        Prop(steps[0], "message").Should().Be(LegacyLine);
        Prop(steps[1], "text").Should().Be(LegacyLine);
        Prop(steps[2], "user_id").Should().Be("{user.id}");
    }

    [Fact]
    public async Task The_graph_cache_the_executor_runs_is_populated_with_all_three_steps()
    {
        (RaidResponseFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(FreshStub());
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        Guid pipelineId = db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value;
        string? graph = db.Pipelines.Single(p => p.Id == pipelineId).GraphJsonCache;
        graph.Should().Contain("send_message").And.Contain("play_tts").And.Contain("shoutout");
    }

    [Fact]
    public async Task A_row_the_channel_saved_itself_is_left_alone()
    {
        (RaidResponseFlowSeeder seeder, SeedTestDbContext db) = Build();
        EventResponse edited = FreshStub();
        edited.FollowsPlatformDefault = false;
        edited.Message = "Welcome in, raiders!";
        db.EventResponses.Add(edited);
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        EventResponse after = db.EventResponses.Single(r => r.EventType == EventType);
        after.ResponseType.Should().Be("chat_message");
        after.Message.Should().Be("Welcome in, raiders!");
        after.PipelineId.Should().BeNull();
        db.PipelineSteps.Should().BeEmpty();
    }

    [Fact]
    public async Task A_row_already_pointing_at_a_built_pipeline_keeps_its_pipeline_and_steps()
    {
        (RaidResponseFlowSeeder seeder, SeedTestDbContext db) = Build();
        Guid pipelineId = Guid.CreateVersion7();
        db.Pipelines.Add(
            new Pipeline
            {
                Id = pipelineId,
                BroadcasterId = Tenant,
                Name = "Raid response",
                TriggerKind = "event",
                IsEnabled = true,
            }
        );
        db.PipelineSteps.Add(
            new PipelineStep
            {
                Id = Guid.CreateVersion7(),
                PipelineId = pipelineId,
                BroadcasterId = Tenant,
                ActionType = "send_message",
                ConfigJson = """{"message":"mine"}""",
                IsEnabled = true,
            }
        );
        EventResponse own = FreshStub();
        own.ResponseType = "pipeline";
        own.PipelineId = pipelineId;
        own.FollowsPlatformDefault = false;
        db.EventResponses.Add(own);
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        db.EventResponses.Single(r => r.EventType == EventType).PipelineId.Should().Be(pipelineId);
        db.PipelineSteps.Should().ContainSingle().Which.ConfigJson.Should().Contain("mine");
        db.Pipelines.Should().ContainSingle();
    }

    [Fact]
    public async Task Seeding_twice_keeps_the_same_pipeline()
    {
        (RaidResponseFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(FreshStub());
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);
        Guid first = db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value;
        await seeder.SeedAsync(Tenant);

        db.EventResponses.Single(r => r.EventType == EventType).PipelineId.Should().Be(first);
        db.Pipelines.Should().ContainSingle();
        db.PipelineSteps.Should().HaveCount(3);
    }
}
