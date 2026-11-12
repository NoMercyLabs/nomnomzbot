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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
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
/// S-PIPE-TREE-d2b-UI: <see cref="Pipeline.ParameterNamesJson"/> is stored on the entity and read by the
/// engine's named-arg binding (S-PIPE-TREE-d2b(a)), but the response DTOs never surfaced it — the pipeline
/// builder's `run_pipeline` target picker had no way to know a callee's declared parameter names. These tests
/// pin the DTO mapping: the declared names round-trip through both <see cref="PipelineService.GetAsync"/>
/// (full detail) and <see cref="PipelineService.ListAsync"/> (list item), and a pipeline with no declared
/// names maps to a null/empty list rather than a malformed shape.
/// </summary>
public sealed class PipelineServiceParameterNamesTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000c0703");

    private static (PipelineService Service, PipelineTestRunDbContext Db) Build()
    {
        PipelineTestRunDbContext db = PipelineTestRunDbContext.New();
        CommandConfigValidator validator = new([], new TemplateHelperValidator());
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

    private static async Task<Guid> SeedPipelineAsync(
        PipelineTestRunDbContext db,
        string? parameterNamesJson
    )
    {
        PipelineEntity pipeline = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "callee-with-params",
            TriggerKind = "manual",
            IsEnabled = true,
            GraphJsonCache = null,
            ParameterNamesJson = parameterNamesJson,
        };
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync();
        return pipeline.Id;
    }

    [Fact]
    public async Task Create_and_get_round_trip_typed_inputs_with_defaults()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();
        PipelineInputDefinition blocks = new("blocks", "list", true, null, "Speech blocks");
        PipelineInputDefinition voice = new(
            "voice",
            "text",
            false,
            JsonSerializer.SerializeToElement("default-voice"),
            "Voice to use"
        );

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new CreatePipelineDto { Name = "speak", Inputs = [blocks, voice] }
        );

        created.IsSuccess.Should().BeTrue();
        Result<PipelineDto> fetched = await service.GetAsync(
            Broadcaster.ToString(),
            created.Value.Id
        );
        fetched.IsSuccess.Should().BeTrue();
        fetched.Value.Inputs.Should().HaveCount(2);
        fetched.Value.Inputs[0].Should().BeEquivalentTo(blocks);
        fetched.Value.Inputs[1].Name.Should().Be(voice.Name);
        fetched.Value.Inputs[1].Type.Should().Be(voice.Type);
        fetched.Value.Inputs[1].Required.Should().BeFalse();
        fetched.Value.Inputs[1].Description.Should().Be(voice.Description);
        fetched.Value.Inputs[1].DefaultValue!.Value.GetString().Should().Be("default-voice");
        db.Pipelines.Single().ParameterNamesJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetAsync_maps_declared_parameter_names_in_order()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();
        Guid pipelineId = await SeedPipelineAsync(
            db,
            JsonSerializer.Serialize(new[] { "target_user", "amount" })
        );

        Result<PipelineDto> result = await service.GetAsync(Broadcaster.ToString(), pipelineId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParameterNames.Should().NotBeNull();
        result.Value.ParameterNames.Should().HaveCount(2);
        result.Value.ParameterNames.Should().ContainInOrder("target_user", "amount");
    }

    [Fact]
    public async Task GetAsync_maps_no_declared_parameters_to_null()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();
        Guid pipelineId = await SeedPipelineAsync(db, parameterNamesJson: null);

        Result<PipelineDto> result = await service.GetAsync(Broadcaster.ToString(), pipelineId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParameterNames.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_maps_declared_parameter_names_onto_the_list_item()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();
        await SeedPipelineAsync(db, JsonSerializer.Serialize(new[] { "reason" }));

        Result<PagedList<PipelineListItemDto>> result = await service.ListAsync(
            Broadcaster.ToString(),
            new PaginationParams { Page = 1, PageSize = 25 }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].ParameterNames.Should().NotBeNull();
        result.Value.Items[0].ParameterNames.Should().ContainSingle("reason");
    }
}
