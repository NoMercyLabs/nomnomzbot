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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the pipeline action-catalogue endpoint (commands-pipelines.md §3.13): it emits a descriptor for every
/// registered <see cref="ICommandAction"/> and <see cref="ICommandCondition"/> — sourced from the DI registry so
/// the builder palette can never drift — with the default interface members (Category/Description) applied.
/// </summary>
public sealed class PipelinesControllerCatalogueTests
{
    private sealed class FakeAction(string type, string category) : ICommandAction
    {
        public string ActionType => type;
        public string Category => category;

        // Description is intentionally NOT overridden — the test asserts the default (=> ActionType) member.
        public Task<Application.Abstractions.Pipeline.ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(Application.Abstractions.Pipeline.ActionResult.Success(string.Empty));
    }

    private sealed class FakeCondition(string type) : ICommandCondition
    {
        public string ConditionType => type;

        public Task<bool> EvaluateAsync(
            PipelineExecutionContext ctx,
            ConditionDefinition condition
        ) => Task.FromResult(true);
    }

    [Fact]
    public void Catalogue_lists_every_registered_action_and_condition_ordered_by_category_then_type()
    {
        List<ICommandAction> actions =
        [
            new FakeAction("timeout", "moderation"),
            new FakeAction("send_message", "chat"),
        ];
        List<ICommandCondition> conditions = [new FakeCondition("user_role")];
        PipelinesController controller = new(
            Substitute.For<IPipelineService>(),
            Substitute.For<ICommandConfigValidator>(),
            actions,
            conditions
        );

        IActionResult result = controller.ListActionCatalogue("chan");

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<PipelineCatalogueDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<PipelineCatalogueDto>>()
            .Subject;
        PipelineCatalogueDto data = body.Data!;

        data.Actions.Should().HaveCount(2);
        // Ordered category→type: chat/send_message before moderation/timeout.
        data.Actions[0].Type.Should().Be("send_message");
        data.Actions[0].Category.Should().Be("chat");
        // Description falls back to the default interface member (= ActionType).
        data.Actions[0].Description.Should().Be("send_message");
        data.Actions[1].Type.Should().Be("timeout");
        data.Conditions.Should().ContainSingle().Which.Type.Should().Be("user_role");
    }
}
