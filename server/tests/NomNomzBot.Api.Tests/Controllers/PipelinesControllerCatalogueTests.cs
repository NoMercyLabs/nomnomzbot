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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the pipeline action-catalogue endpoint (commands-pipelines.md §3.13): it emits a descriptor for every
/// registered <see cref="ICommandAction"/> and <see cref="ICommandCondition"/> — sourced from the DI registry so
/// the builder palette can never drift — carrying each action's <see cref="LocalizedText"/> Category/Description
/// KEYS through to the wire DTO unchanged (S-SCHEMA-I18N-d — every action supplies its own, there is no default).
/// </summary>
public sealed class PipelinesControllerCatalogueTests
{
    private sealed class FakeAction(
        string type,
        string category,
        IReadOnlyList<PipelineActionFieldDescriptor>? fields = null
    ) : ICommandAction
    {
        public string ActionType => type;
        public LocalizedText Category => new(category);
        public LocalizedText Description => new($"pipeline.{type}.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields => fields ?? [];

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
            Substitute.For<IPipelineTestRunService>(),
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
        data.Actions[0].Category.Key.Should().Be("chat");
        data.Actions[0].Description.Key.Should().Be("pipeline.send_message.description");
        data.Actions[1].Type.Should().Be("timeout");
        data.Conditions.Should().ContainSingle().Which.Type.Should().Be("user_role");
    }

    [Fact]
    public void Catalogue_maps_field_descriptors_including_kind_and_enum_options()
    {
        List<ICommandAction> actions =
        [
            new FakeAction(
                "timeout",
                "moderation",
                [
                    new("user_id", PipelineActionFieldKind.TwitchUser, Required: false),
                    new("duration", PipelineActionFieldKind.Number, Required: true),
                    new(
                        "mode",
                        PipelineActionFieldKind.Enum,
                        Required: true,
                        Options: ["off", "track", "context"]
                    ),
                ]
            ),
        ];
        PipelinesController controller = new(
            Substitute.For<IPipelineService>(),
            Substitute.For<IPipelineTestRunService>(),
            Substitute.For<ICommandConfigValidator>(),
            actions,
            []
        );

        IActionResult result = controller.ListActionCatalogue("chan");

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        PipelineCatalogueDto data = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<PipelineCatalogueDto>>()
            .Subject.Data!;

        IReadOnlyList<PipelineActionFieldDto> fields = data.Actions[0].Fields;
        fields.Should().HaveCount(3);
        fields[0].Name.Should().Be("user_id");
        fields[0].Kind.Should().Be("twitch_user");
        fields[0].Required.Should().BeFalse();
        fields[1].Kind.Should().Be("number");
        fields[1].Required.Should().BeTrue();
        fields[2].Kind.Should().Be("enum");
        fields[2].Options.Should().BeEquivalentTo(["off", "track", "context"]);
    }
}
