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
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Tests.Content;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// The <c>channel.raid.out</c> reactive commit flow: stop the stream, pause the music, confirm the
/// raid in chat — fired by Twitch's own raid-out signal instead of a guessed elapsed-time offset.
/// The critical regression these tests guard: <c>EventResponseExecutor.RunPipelineAsync</c> executes
/// <c>Pipeline.GraphJsonCache</c>, not <c>PipelineSteps</c> directly — an empty cache runs zero steps
/// silently, which is exactly the bug an earlier draft of <see cref="RaidCommitFlowSeeder"/> shipped
/// with (caught before any live test, but the failure mode has no other guard).
/// </summary>
public sealed class RaidCommitFlowSeederTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4b00-4444-7000-8000-000000000001");
    private const string EventType = "channel.raid.out";

    private static (RaidCommitFlowSeeder Seeder, SeedTestDbContext Db) Build()
    {
        SeedTestDbContext db = SeedTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Tenant,
                Name = "raid-commit-test-channel",
                NameNormalized = "raid-commit-test-channel",
            }
        );
        db.SaveChanges();
        return (new RaidCommitFlowSeeder(db), db);
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

    [Fact]
    public async Task An_untouched_default_stub_gets_wired_to_a_built_pipeline()
    {
        (RaidCommitFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        db.SaveChanges();

        await seeder.SeedAsync(Tenant);

        EventResponse response = db.EventResponses.Single(r => r.BroadcasterId == Tenant);
        response.ResponseType.Should().Be("pipeline");
        response.IsEnabled.Should().BeTrue();
        response.PipelineId.Should().NotBeNull();

        Pipeline pipeline = db.Pipelines.Single(p => p.Id == response.PipelineId);
        pipeline.IsEnabled.Should().BeTrue();
    }

    /// <summary>
    /// The exact bug caught before any live test: <c>EventResponseExecutor</c> executes the pipeline's
    /// <see cref="Pipeline.GraphJsonCache"/>, never its <see cref="PipelineStep"/> rows directly. A
    /// seeder that inserts steps but forgets to build the cache produces a pipeline that silently runs
    /// zero actions — the raid confirmation, stream stop, and music pause would just never happen.
    /// </summary>
    [Fact]
    public async Task The_wired_pipelines_graph_cache_is_actually_populated_not_left_empty()
    {
        (RaidCommitFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        db.SaveChanges();

        await seeder.SeedAsync(Tenant);

        Guid pipelineId = db
            .EventResponses.Single(r => r.BroadcasterId == Tenant)
            .PipelineId!.Value;
        string? cache = db.Pipelines.Single(p => p.Id == pipelineId).GraphJsonCache;

        cache.Should().NotBeNullOrWhiteSpace();
        cache.Should().NotBe("{}");
        cache.Should().Contain("obs_streaming");
    }

    [Fact]
    public async Task The_stream_stops_and_music_pauses_before_the_raid_is_confirmed_in_chat()
    {
        (RaidCommitFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        db.SaveChanges();

        await seeder.SeedAsync(Tenant);

        Guid pipelineId = db
            .EventResponses.Single(r => r.BroadcasterId == Tenant)
            .PipelineId!.Value;
        List<PipelineStep> steps = StepsOf(db, pipelineId);

        steps.Should().HaveCount(3);
        steps[0].ActionType.Should().Be("obs_streaming");
        steps[1].ActionType.Should().Be("music_pause");
        steps[2].ActionType.Should().Be("send_message");
        steps[2].ConfigJson.Should().Contain("{user}");
    }

    [Fact]
    public async Task Stopping_the_stream_and_pausing_the_music_each_continue_on_error_independently()
    {
        (RaidCommitFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        db.SaveChanges();

        await seeder.SeedAsync(Tenant);

        Guid pipelineId = db
            .EventResponses.Single(r => r.BroadcasterId == Tenant)
            .PipelineId!.Value;
        List<PipelineStep> steps = StepsOf(db, pipelineId);

        steps.Single(s => s.ActionType == "obs_streaming").ContinueOnError.Should().BeTrue();
        steps.Single(s => s.ActionType == "music_pause").ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public async Task A_response_already_wired_to_a_built_pipeline_is_never_touched_again()
    {
        (RaidCommitFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(UntouchedStub(Tenant));
        db.SaveChanges();
        await seeder.SeedAsync(Tenant);

        Guid pipelineIdAfterFirstSeed = db
            .EventResponses.Single(r => r.BroadcasterId == Tenant)
            .PipelineId!.Value;
        int stepCountAfterFirstSeed = db.PipelineSteps.Count();

        await seeder.SeedAsync(Tenant);

        db.EventResponses.Single(r => r.BroadcasterId == Tenant)
            .PipelineId.Should()
            .Be(pipelineIdAfterFirstSeed, "a re-seed must never rewire an already-built response");
        db.PipelineSteps.Count().Should().Be(stepCountAfterFirstSeed);
    }

    /// <summary>
    /// Matches the exact state found live on the owner's channel: the legacy bot had hard-coded a
    /// "We have raided out to..." chat message directly onto this event response, with no pipeline.
    /// That customization is the streamer's own and must never be overwritten by the seeder.
    /// </summary>
    [Fact]
    public async Task A_response_carrying_a_non_empty_chat_message_is_left_completely_alone()
    {
        (RaidCommitFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(
            new EventResponse
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                EventType = EventType,
                ResponseType = "chat_message",
                Message = "We have raided out to {user}!",
                IsEnabled = true,
            }
        );
        db.SaveChanges();

        await seeder.SeedAsync(Tenant);

        EventResponse response = db.EventResponses.Single(r => r.BroadcasterId == Tenant);
        response.ResponseType.Should().Be("chat_message");
        response.Message.Should().Be("We have raided out to {user}!");
        response.PipelineId.Should().BeNull();
        db.Pipelines.Should().BeEmpty();
    }

    [Fact]
    public async Task A_response_for_a_different_event_type_is_never_touched()
    {
        (RaidCommitFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(
            new EventResponse
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                EventType = "channel.follow",
                ResponseType = "chat_message",
                Message = null,
                IsEnabled = false,
            }
        );
        db.SaveChanges();

        await seeder.SeedAsync(Tenant);

        db.EventResponses.Single().ResponseType.Should().Be("chat_message");
        db.EventResponses.Single().PipelineId.Should().BeNull();
        db.Pipelines.Should().BeEmpty();
    }
}
