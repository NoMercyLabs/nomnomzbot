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
