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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Persistence;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Bug report (owner, verbatim): "i have my own custom pipelines from my old bot ... but i don't
/// have anything visible when i edit any of them."
///
/// Root cause: <see cref="PipelineEngine"/> executes a pipeline from its normalized
/// <see cref="PipelineStep"/>/<see cref="PipelineStepCondition"/> rows (falling back to
/// <c>GraphJsonCache</c> only when no rows exist — <c>PipelineEngine.cs</c> "Step source
/// priority"), but <see cref="PipelineService.GetAsync"/> / its private <c>ToDto</c> read
/// exclusively from <c>GraphJsonCache</c> and never load or project the <see cref="PipelineStep"/>
/// rows. A pipeline imported straight into the normalized tables (as the owner's old-bot
/// migration does) has real, executable steps but a null <c>GraphJsonCache</c> — so the GET
/// endpoint the pipeline editor calls returns <c>GraphJsonCache: null</c> and the editor renders
/// an empty canvas even though the pipeline runs correctly (or partially — a separate concern)
/// on stream.
/// </summary>
public sealed class PipelineServiceLegacyStepsTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000c0702");

    private static (PipelineService Service, PipelineTestRunDbContext Db) Build()
    {
        PipelineTestRunDbContext db = PipelineTestRunDbContext.New();
        CommandConfigValidator validator = new(
            [
                new FakeAction { ActionType = "send_message" },
                new FakeAction { ActionType = "timeout_user" },
            ],
            new TemplateHelperValidator()
        );
        return (
            new(
                db,
                new PassThroughUnitOfWork(),
                Substitute.For<IEventBus>(),
                validator,
                Substitute.For<IChannelRegistry>()
            ),
            db
        );
    }

    private sealed class FakeAction : ICommandAction
    {
        public required string ActionType { get; init; }

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    /// <summary>
    /// Reproduces the owner's exact shape: a pipeline row plus normalized <see cref="PipelineStep"/>
    /// (and a nested <see cref="PipelineStepCondition"/>) rows written directly to the tables — the
    /// way an old-bot import/migration would seed them — with <c>GraphJsonCache</c> left null,
    /// exactly as <see cref="PipelineService.CreateAsync"/>/<see cref="PipelineService.UpdateAsync"/>
    /// never populate it from steps.
    /// </summary>
    private static async Task<Guid> SeedLegacyPipelineAsync(PipelineTestRunDbContext db)
    {
        PipelineEntity pipeline = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "old-bot-import",
            TriggerKind = "command",
            IsEnabled = true,
            GraphJsonCache = null,
        };
        db.Pipelines.Add(pipeline);

        PipelineStep step0 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipeline.Id,
            BroadcasterId = Broadcaster,
            Order = 0,
            ActionType = "send_message",
            ConfigJson = JsonSerializer.Serialize(
                new { type = "send_message", message = "hello {{user.name}}" }
            ),
            IsEnabled = true,
        };
        db.PipelineSteps.Add(step0);

        PipelineStep step1 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipeline.Id,
            BroadcasterId = Broadcaster,
            Order = 1,
            ActionType = "timeout_user",
            ConfigJson = JsonSerializer.Serialize(new { type = "timeout_user", seconds = 60 }),
            IsEnabled = true,
        };
        db.PipelineSteps.Add(step1);

        PipelineStepCondition condition = new()
        {
            Id = Guid.NewGuid(),
            PipelineStepId = step1.Id,
            BroadcasterId = Broadcaster,
            ConditionType = "user_role",
            Operator = "eq",
            LeftOperand = "role",
            RightOperand = "moderator",
            Negate = false,
            Order = 0,
        };
        db.PipelineStepConditions.Add(condition);

        await db.SaveChangesAsync();
        return pipeline.Id;
    }

    [Fact]
    public async Task GetAsync_returns_every_step_action_and_condition_for_a_legacy_imported_pipeline()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();
        Guid pipelineId = await SeedLegacyPipelineAsync(db);

        Result<PipelineDto> result = await service.GetAsync(Broadcaster.ToString(), pipelineId);

        result.IsSuccess.Should().BeTrue();
        result
            .Value.GraphJsonCache.Should()
            .NotBeNull(
                "the editor renders from GraphJsonCache; a legacy pipeline with real DB steps must not"
                    + " arrive as null/empty"
            );

        JsonElement steps = result.Value.GraphJsonCache!.Value.GetProperty("steps");
        steps.GetArrayLength().Should().Be(2);

        JsonElement first = steps[0];
        first.GetProperty("action").GetProperty("type").GetString().Should().Be("send_message");
        first
            .GetProperty("action")
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("hello {{user.name}}");

        JsonElement second = steps[1];
        second.GetProperty("action").GetProperty("type").GetString().Should().Be("timeout_user");
        second.GetProperty("action").GetProperty("seconds").GetInt32().Should().Be(60);

        JsonElement condition = second.GetProperty("condition");
        condition.GetProperty("type").GetString().Should().Be("user_role");
        condition.GetProperty("left").GetString().Should().Be("role");
        condition.GetProperty("right").GetString().Should().Be("moderator");
        condition.GetProperty("operator").GetString().Should().Be("eq");
        condition.GetProperty("negate").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Second, independent defect in the same bug report: the wire JSON key for
    /// <see cref="PipelineDto.GraphJsonCache"/> is <c>"graph"</c> (<c>[JsonPropertyName("graph")]</c>, matching
    /// the KMP client's <c>PipelineDetail.graph</c> field and the original spec's <c>PipelineGraphDto Graph</c>
    /// naming) — NOT the ASP.NET camelCase default of <c>"graphJsonCache"</c> the C# property name would produce
    /// unattributed. Before this attribute, GET/POST/PUT all serialized/bound the field as
    /// <c>graphJsonCache</c>, so the dashboard's request body (<c>{"graph": {...}}</c>) never populated it on
    /// create/update, and the dashboard's response reader (looking for <c>"graph"</c>) never found it on read —
    /// independently of the DB-steps-vs-cache defect covered above, and enough on its own to blank every
    /// pipeline's editor, new or legacy. Proven here against the exact <see cref="JsonSerializerOptions"/> the
    /// API host configures (<c>Program.cs</c> — camelCase policy).
    /// </summary>
    [Fact]
    public void PipelineDto_and_request_Dtos_serialize_the_graph_field_as_graph_not_graphJsonCache()
    {
        JsonSerializerOptions apiOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        PipelineDto dto = new(
            Guid.NewGuid(),
            Broadcaster.ToString(),
            "wire-shape-check",
            null,
            true,
            "manual",
            JsonSerializer.SerializeToElement(new { steps = Array.Empty<object>() }),
            0,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            null
        );

        string responseJson = JsonSerializer.Serialize(dto, apiOptions);
        responseJson.Should().Contain("\"graph\":");
        responseJson.Should().NotContain("graphJsonCache");

        string requestJson = """{"name":"from-dashboard","graph":{"steps":[]}}""";
        CreatePipelineDto? create = JsonSerializer.Deserialize<CreatePipelineDto>(
            requestJson,
            apiOptions
        );
        create.Should().NotBeNull();
        create
            .GraphJsonCache.Should()
            .NotBeNull(
                "the dashboard's create/update request body sends the field as \"graph\"; the DTO must bind it"
            );
    }

    [Fact]
    public async Task GetAsync_prefers_the_stored_graph_cache_when_it_is_present_and_in_sync()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "authored-in-app",
                GraphJsonCache = JsonSerializer.SerializeToElement(
                    new { steps = new[] { new { action = new { type = "send_message" } } } }
                ),
            }
        );
        created.IsSuccess.Should().BeTrue();

        Result<PipelineDto> result = await service.GetAsync(
            Broadcaster.ToString(),
            created.Value.Id
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.GraphJsonCache!.Value.GetProperty("steps").GetArrayLength().Should().Be(1);
    }
}
