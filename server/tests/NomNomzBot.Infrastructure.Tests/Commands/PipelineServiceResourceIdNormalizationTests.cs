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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Persistence;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Root-cause fix for the qtkitte "!hug" bug (2026-09-07): a run_code step's code_script_id was persisted as
/// the dashboard picker's wire form (a 26-char ULID, UlidGuidJsonConverter) instead of a raw Guid, so
/// RunCodeAction's Guid.TryParse always failed the step at execution — every real chat trigger came back
/// "partially_failed" with "run_code requires a valid code_script_id.", silently, since RunCodeAction never
/// logs. Proves PipelineService.CreateAsync/UpdateAsync now decode that wire form down to the canonical Guid
/// string in the PipelineStep row it persists, so every future run resolves the reference correctly.
/// </summary>
public sealed class PipelineServiceResourceIdNormalizationTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000d0703");
    private static readonly Guid ScriptId = Guid.Parse("0192a000-0000-7000-8000-00000000d0aa");

    private sealed class FakeRunCodeAction : ICommandAction
    {
        public string ActionType => "run_code";

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new("code_script_id", PipelineActionFieldKind.ResourceId, Required: true)];

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    private static (PipelineService Service, PipelineTestRunDbContext Db) Build()
    {
        PipelineTestRunDbContext db = PipelineTestRunDbContext.New();
        CommandConfigValidator validator = new(
            [new FakeRunCodeAction()],
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

    [Fact]
    public async Task CreateAsync_decodes_a_ulid_form_code_script_id_to_the_canonical_guid_string()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();
        string ulid = OwnedIdCodec.Encode(ScriptId);

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "hug",
                GraphJsonCache = JsonSerializer.SerializeToElement(
                    new
                    {
                        steps = new[]
                        {
                            new { action = new { type = "run_code", code_script_id = ulid } },
                        },
                    }
                ),
            }
        );

        created.IsSuccess.Should().BeTrue();

        PipelineStep persisted = await db.PipelineSteps.SingleAsync(s =>
            s.PipelineId == created.Value.Id
        );
        persisted.ConfigJson.Should().Contain(ScriptId.ToString());
        persisted.ConfigJson.Should().NotContain(ulid);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_code_script_id_that_is_neither_a_ulid_nor_a_guid()
    {
        (PipelineService service, _) = Build();

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "hug",
                GraphJsonCache = JsonSerializer.SerializeToElement(
                    new
                    {
                        steps = new[]
                        {
                            new { action = new { type = "run_code", code_script_id = "garbage" } },
                        },
                    }
                ),
            }
        );

        created.IsSuccess.Should().BeFalse();
        created.ErrorCode.Should().Be("INVALID_RESOURCE_ID");
    }
}
