// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Tests.Content;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// The <c>channel.raid.start</c> countdown flow: switch to the ending scene and call the raid out in
/// chat, fired when Twitch CONFIRMS the raid has started.
///
/// <para>These steps used to run inside the <c>!raid</c> command itself, so a raid Twitch rejected —
/// target offline, bad name, no permission — still switched the scene and posted to chat. The three
/// raid surfaces are now each driven by their own signal: the command starts a validated raid,
/// <c>channel.raid.start</c> runs the countdown, and <c>channel.raid.out</c> (the raid executing)
/// stops the stream.</para>
/// </summary>
public sealed class RaidStartFlowSeederTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4b00-5555-7000-8000-000000000001");
    private const string EventType = "channel.raid.start";

    private static (RaidStartFlowSeeder Seeder, SeedTestDbContext Db) Build()
    {
        SeedTestDbContext db = SeedTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Tenant,
                Name = "raid-start-test-channel",
                NameNormalized = "raid-start-test-channel",
            }
        );
        db.SaveChanges();
        return (new RaidStartFlowSeeder(db), db);
    }

    private static EventResponse UntouchedStub(Guid tenant) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = tenant,
            EventType = EventType,
            ResponseType = "chat_message",
            Message = null,
            IsEnabled = false,
        };

    private static List<PipelineStep> StepsOf(SeedTestDbContext db, Guid pipelineId) =>
        db.PipelineSteps.Where(s => s.PipelineId == pipelineId).OrderBy(s => s.Order).ToList();

    private static string MessageOf(PipelineStep step) =>
        JsonDocument.Parse(step.ConfigJson).RootElement.GetProperty("message").GetString()!;

    private const string ShippedCallOut =
        """{"message":"We're heading out to {user}, thanks for watching!"}""";

    /// <summary>A start pipeline in the only shape ever seeded before the recap step existed
    /// (d3a60e778, unchanged by 07639dcea).</summary>
    private static Guid AddPipelineWith(
        SeedTestDbContext db,
        params (string Type, string Config, bool ContinueOnError)[] steps
    )
    {
        Guid pipelineId = Guid.CreateVersion7();
        int order = 0;
        List<PipelineStep> rows =
        [
            .. steps.Select(s => new PipelineStep
            {
                Id = Guid.CreateVersion7(),
                PipelineId = pipelineId,
                BroadcasterId = Tenant,
                ActionType = s.Type,
                ConfigJson = s.Config,
                ContinueOnError = s.ContinueOnError,
                Order = order++,
                IsEnabled = true,
            }),
        ];
        db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = Tenant,
                Name = "Raid starting",
                TriggerKind = "event",
                IsEnabled = true,
                GraphJsonCache = Infrastructure.Commands.PipelineGraphBuilder.BuildGraphJson(rows),
            }
        );
        db.PipelineSteps.AddRange(rows);
        db.EventResponses.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                EventType = EventType,
                ResponseType = "pipeline",
                PipelineId = pipelineId,
                IsEnabled = true,
            }
        );
        db.SaveChanges();
        return pipelineId;
    }

    [Fact]
    public async Task A_fresh_start_flow_opens_with_the_recap_so_it_covers_the_stream_up_to_the_command()
    {
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        Guid pipelineId = db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value;
        List<PipelineStep> steps = StepsOf(db, pipelineId);
        steps
            .Select(s => s.ActionType)
            .Should()
            .Equal("show_raid_recap", "obs_switch_scene", "send_message");
        steps[0]
            .ContinueOnError.Should()
            .BeTrue("a widget nobody has open must never block the countdown");
        db.Pipelines.Single(p => p.Id == pipelineId)
            .GraphJsonCache.Should()
            .Contain("show_raid_recap");
    }

    [Fact]
    public async Task An_untouched_pipeline_of_the_oldest_seeded_shape_gets_the_recap_as_its_first_step()
    {
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        Guid pipelineId = AddPipelineWith(
            db,
            ("obs_switch_scene", """{"scene":"Ending"}""", true),
            ("send_message", ShippedCallOut, false)
        );

        await seeder.SeedAsync(Tenant);

        List<PipelineStep> steps = StepsOf(db, pipelineId);
        steps
            .Select(s => s.ActionType)
            .Should()
            .Equal("show_raid_recap", "obs_switch_scene", "send_message");
        steps.Select(s => s.Order).Should().Equal(0, 1, 2);
        steps[0].ConfigJson.Should().Be("{}");
        steps[0].ContinueOnError.Should().BeTrue();
        steps[2].ConfigJson.Should().Be(ShippedCallOut);
        db.Pipelines.Single(p => p.Id == pipelineId)
            .GraphJsonCache.Should()
            .Contain("show_raid_recap");
        db.Pipelines.Should().ContainSingle();
    }

    [Fact]
    public async Task Upgrading_twice_adds_the_recap_step_only_once()
    {
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        Guid pipelineId = AddPipelineWith(
            db,
            ("obs_switch_scene", """{"scene":"Ending"}""", true),
            ("send_message", ShippedCallOut, false)
        );

        await seeder.SeedAsync(Tenant);
        await seeder.SeedAsync(Tenant);

        StepsOf(db, pipelineId).Count(s => s.ActionType == "show_raid_recap").Should().Be(1);
    }

    [Fact]
    public async Task A_pipeline_the_streamer_edited_gets_no_recap_step_and_keeps_its_graph()
    {
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        Guid pipelineId = AddPipelineWith(
            db,
            ("obs_switch_scene", """{"scene":"BRB"}""", true),
            ("send_message", ShippedCallOut, false)
        );
        string? cacheBefore = db.Pipelines.Single(p => p.Id == pipelineId).GraphJsonCache;

        await seeder.SeedAsync(Tenant);

        StepsOf(db, pipelineId)
            .Select(s => s.ActionType)
            .Should()
            .Equal("obs_switch_scene", "send_message");
        db.Pipelines.Single(p => p.Id == pipelineId).GraphJsonCache.Should().Be(cacheBefore);
    }

    [Fact]
    public async Task An_untouched_default_stub_gets_wired_to_a_built_pipeline()
    {
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        EventResponse response = db.EventResponses.Single(r => r.EventType == EventType);
        response.ResponseType.Should().Be("pipeline");
        response.PipelineId.Should().NotBeNull();
        response.IsEnabled.Should().BeTrue();
        StepsOf(db, response.PipelineId!.Value).Should().NotBeEmpty();
    }

    [Fact]
    public async Task The_wired_pipelines_graph_cache_is_actually_populated_not_left_empty()
    {
        // EventResponseExecutor runs Pipeline.GraphJsonCache, not PipelineSteps — an empty cache runs
        // zero steps in total silence, which is the failure mode this whole family exists to prevent.
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        Guid pipelineId = db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value;
        Pipeline pipeline = db.Pipelines.Single(p => p.Id == pipelineId);
        pipeline.GraphJsonCache.Should().NotBeNullOrWhiteSpace();
        pipeline.GraphJsonCache.Should().NotBe("{}");
    }

    [Fact]
    public async Task The_scene_switches_before_the_call_out_reaches_chat()
    {
        // Order matters and is asserted as order, not as membership: announcing the raid while the
        // outgoing scene is still up is the wrong way round for anyone watching.
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        List<PipelineStep> steps = StepsOf(
            db,
            db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value
        );
        steps
            .Select(s => s.ActionType)
            .Should()
            .Equal("show_raid_recap", "obs_switch_scene", "send_message");
    }

    [Fact]
    public async Task A_broken_OBS_never_stops_the_chat_call_out()
    {
        // Confirmed live 2026-09-01 in the old command-driven version: "OBS connection closed" on this
        // one step killed the entire rest of the raid flow while Twitch's clock kept running.
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        List<PipelineStep> steps = StepsOf(
            db,
            db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value
        );
        steps.Single(s => s.ActionType == "obs_switch_scene").ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public async Task The_call_out_names_the_target_from_the_event_not_the_typed_command_argument()
    {
        // {args.1} was whatever the streamer typed, capitalisation and typos included, and it was posted
        // even when Twitch rejected the raid. {user} is the target Twitch itself reported.
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        PipelineStep message = StepsOf(
                db,
                db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value
            )
            .Single(s => s.ActionType == "send_message");

        MessageOf(message).Should().Contain("{user}");
        MessageOf(message).Should().NotContain("{args.");
    }

    [Fact]
    public async Task The_countdown_flow_never_stops_the_stream_or_the_music()
    {
        // Those belong to channel.raid.out, which fires when the raid has actually executed. Doing them
        // here is the original bug: the broadcast ended at the start of the countdown.
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        List<PipelineStep> steps = StepsOf(
            db,
            db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value
        );
        steps.Should().NotContain(s => s.ActionType == "obs_streaming");
        steps.Should().NotContain(s => s.ActionType == "music_pause");
    }

    [Fact]
    public async Task A_streamers_own_chat_message_is_never_overwritten()
    {
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        EventResponse theirs = UntouchedStub(Tenant);
        theirs.Message = "Off we go!";
        db.EventResponses.Add(theirs);
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        EventResponse after = db.EventResponses.Single(r => r.EventType == EventType);
        after.ResponseType.Should().Be("chat_message");
        after.Message.Should().Be("Off we go!");
        after.PipelineId.Should().BeNull();
    }

    [Fact]
    public async Task Seeding_twice_leaves_exactly_one_pipeline_behind()
    {
        // Idempotence asserted on the PIPELINE ID, not just a count: a second run that built a fresh
        // pipeline and repointed the response would keep the count at one per response while orphaning
        // the first pipeline and silently discarding any edit made to it.
        (RaidStartFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);
        Guid first = db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value;

        await seeder.SeedAsync(Tenant);

        db.EventResponses.Single(r => r.EventType == EventType)
            .PipelineId!.Value.Should()
            .Be(first, "a second run must adopt the pipeline it already built");
        db.Pipelines.Count(p => p.BroadcasterId == Tenant).Should().Be(1);
    }
}
