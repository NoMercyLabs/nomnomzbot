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
/// Older cheer flows and the imported resub flow spoke only the viewer's message, so the bot never called out the
/// amount or the duration. The heal turns that step into the whole announcement and leaves custom steps alone.
/// </summary>
public sealed class SpokenAnnouncementHealSeederTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4b00-8888-7000-8000-000000000001");

    private static PipelineStep Step(Guid pipelineId, int order, string action, string config) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            PipelineId = pipelineId,
            BroadcasterId = Tenant,
            ActionType = action,
            ConfigJson = config,
            Order = order,
            IsEnabled = true,
        };

    private static async Task<(
        SeedTestDbContext Db,
        Guid PipelineId,
        PipelineStep Spoken
    )> SeedAsync(string name, string spokenConfig, string? thanks)
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
        Guid pipelineId = Guid.CreateVersion7();
        db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = Tenant,
                Name = name,
                TriggerKind = "event",
                IsEnabled = true,
                GraphJsonCache = "{}",
            }
        );
        if (thanks is not null)
            db.PipelineSteps.Add(Step(pipelineId, 0, "send_message", thanks));
        PipelineStep spoken = Step(pipelineId, 1, "play_tts", spokenConfig);
        spoken.Conditions =
        [
            new()
            {
                PipelineStepId = spoken.Id,
                BroadcasterId = Tenant,
                ConditionType = "comparison",
                LeftOperand = "{message}",
                Operator = "ne",
                RightOperand = "",
                Order = 0,
            },
        ];
        db.PipelineSteps.Add(spoken);
        await db.SaveChangesAsync();

        return (db, pipelineId, spoken);
    }

    private static string Text(PipelineStep step) =>
        JsonDocument.Parse(step.ConfigJson).RootElement.GetProperty("text").GetString()!;

    [Fact]
    public async Task A_cheer_step_that_speaks_only_the_message_speaks_the_whole_thank_you()
    {
        (SeedTestDbContext db, _, PipelineStep spoken) = await SeedAsync(
            "Cheer response",
            """{"as":"viewer","text":"{message}"}""",
            """{"message":"Thank you {user} for the {bits} bits!{also_said}"}"""
        );

        await new SpokenAnnouncementHealSeeder(db).SeedAsync();

        PipelineStep healed = db.PipelineSteps.Single(s => s.Id == spoken.Id);
        Text(healed).Should().Be("Thank you {user} for the {bits} bits!{also_said}");
        JsonDocument
            .Parse(healed.ConfigJson)
            .RootElement.TryGetProperty("as", out JsonElement _)
            .Should()
            .BeFalse(
                "the announcement is the bot's; play_tts gives the viewer's words their own voice"
            );
        db.PipelineStepConditions.Count(c => c.PipelineStepId == spoken.Id)
            .Should()
            .Be(0, "the announcement is spoken even when the viewer wrote no message");
    }

    [Fact]
    public async Task The_imported_resub_step_speaks_the_duration_and_rebuilds_the_graph()
    {
        (SeedTestDbContext db, Guid pipelineId, PipelineStep spoken) = await SeedAsync(
            "Resub response",
            """{"as":"viewer","text":"{message}"}""",
            thanks: null
        );

        await new SpokenAnnouncementHealSeeder(db).SeedAsync();

        Text(db.PipelineSteps.Single(s => s.Id == spoken.Id))
            .Should()
            .Be("{user} resubscribed for {months} months! Thank you!{also_said}");
        db.Pipelines.Single(p => p.Id == pipelineId)
            .GraphJsonCache.Should()
            .Contain("{months}", "the compiled graph is what runs, so it carries the new text");
    }

    [Fact]
    public async Task A_step_the_channel_customized_is_left_alone()
    {
        (SeedTestDbContext db, _, PipelineStep spoken) = await SeedAsync(
            "Resub response",
            """{"as":"broadcaster","text":"{message}"}""",
            thanks: null
        );
        string before = spoken.ConfigJson;

        await new SpokenAnnouncementHealSeeder(db).SeedAsync();

        db.PipelineSteps.Single(s => s.Id == spoken.Id).ConfigJson.Should().Be(before);
    }
}
