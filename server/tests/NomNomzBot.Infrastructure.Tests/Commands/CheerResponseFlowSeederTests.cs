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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Tests.Content;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// A new channel's cheer response thanks the viewer in chat, quotes their own message, and reads that message
/// in their own saved voice. The quote is the <c>{also_said}</c> clause (empty without a message) and the
/// spoken step only runs when there is a message, so an empty cheer gives neither a quote nor viewer TTS.
/// </summary>
public sealed class CheerResponseFlowSeederTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4b00-7777-7000-8000-000000000001");
    private const string EventType = "channel.cheer";

    private static (CheerResponseFlowSeeder Seeder, SeedTestDbContext Db) Build()
    {
        SeedTestDbContext db = SeedTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Tenant,
                Name = "cheer-response-test-channel",
                NameNormalized = "cheer-response-test-channel",
            }
        );
        db.SaveChanges();
        return (new CheerResponseFlowSeeder(db), db);
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
    public async Task A_fresh_channels_cheer_thanks_in_chat_with_the_quote_then_speaks_the_viewers_words()
    {
        (CheerResponseFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(FreshStub());
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        EventResponse response = db.EventResponses.Single(r => r.EventType == EventType);
        response.ResponseType.Should().Be("pipeline");
        response.IsEnabled.Should().BeTrue();
        response.FollowsPlatformDefault.Should().BeFalse();

        List<PipelineStep> steps = StepsOf(db);
        steps.Select(s => s.ActionType).Should().Equal("send_message", "play_tts");
        Prop(steps[0], "message")
            .Should()
            .BeOneOf(EventResponseToneCatalog.Get(PersonalityTone.Informative, EventType))
            .And.EndWith("{also_said}");
        Prop(steps[1], "text").Should().Be("{message}");
        JsonDocument
            .Parse(steps[1].ConfigJson)
            .RootElement.TryGetProperty("as", out JsonElement _)
            .Should()
            .BeFalse("a blank 'as' with {message} is the viewer's own voice");
    }

    [Fact]
    public async Task The_spoken_step_runs_only_when_the_cheer_has_a_message()
    {
        (CheerResponseFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(FreshStub());
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        Guid pipelineId = db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value;
        string graph = db.Pipelines.Single(p => p.Id == pipelineId).GraphJsonCache!;
        using JsonDocument document = JsonDocument.Parse(graph);
        List<JsonElement> steps = document
            .RootElement.GetProperty("steps")
            .EnumerateArray()
            .ToList();
        steps[0].GetProperty("condition").ValueKind.Should().Be(JsonValueKind.Null);
        JsonElement condition = steps[1].GetProperty("condition");
        condition.GetProperty("left").GetString().Should().Be("{message}");
        condition.GetProperty("operator").GetString().Should().Be("ne");
        condition.GetProperty("right").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task A_row_the_channel_saved_itself_is_left_alone()
    {
        (CheerResponseFlowSeeder seeder, SeedTestDbContext db) = Build();
        EventResponse edited = FreshStub();
        edited.FollowsPlatformDefault = false;
        edited.Message = "Thanks for the bits!";
        db.EventResponses.Add(edited);
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        EventResponse after = db.EventResponses.Single(r => r.EventType == EventType);
        after.ResponseType.Should().Be("chat_message");
        after.Message.Should().Be("Thanks for the bits!");
        after.PipelineId.Should().BeNull();
        db.PipelineSteps.Should().BeEmpty();
    }

    [Fact]
    public async Task Seeding_twice_keeps_the_same_pipeline()
    {
        (CheerResponseFlowSeeder seeder, SeedTestDbContext db) = Build();
        db.EventResponses.Add(FreshStub());
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);
        Guid first = db.EventResponses.Single(r => r.EventType == EventType).PipelineId!.Value;
        await seeder.SeedAsync(Tenant);

        db.EventResponses.Single(r => r.EventType == EventType).PipelineId.Should().Be(first);
        db.Pipelines.Should().ContainSingle();
        db.PipelineSteps.Should().HaveCount(2);
    }
}
