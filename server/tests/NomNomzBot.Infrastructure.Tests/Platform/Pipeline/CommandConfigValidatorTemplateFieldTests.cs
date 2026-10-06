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
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S042b: extends the S042 save-time helper guard from the 3 original surfaces (commands, event
/// responses, timers) onto pipeline action fields — the biggest authoring surface. Only fields an
/// action itself declares <see cref="PipelineActionFieldDescriptor.Templated"/> get checked; a
/// non-templated field (e.g. a numeric delay) is never touched even when it happens to contain
/// brace-shaped text.
/// </summary>
public sealed class CommandConfigValidatorTemplateFieldTests
{
    private sealed class FakeTemplatedAction : ICommandAction
    {
        public string ActionType => "send_message";

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new("message", PipelineActionFieldKind.Text, Required: true, Templated: true)];

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    private sealed class FakeNonTemplatedNumberAction : ICommandAction
    {
        public string ActionType => "timeout_user";

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new("seconds", PipelineActionFieldKind.Number, Required: true)];

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    private sealed class FakeVariableDeclaringAction : ICommandAction
    {
        public string ActionType => "pick_from_list";

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [
                new("list", PipelineActionFieldKind.Text, Required: true),
                new(
                    "variable",
                    PipelineActionFieldKind.Text,
                    DeclaresVariable: true,
                    DeclaredVariableDefault: "pick"
                ),
            ];

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    private sealed class FakeTemplatedTtsAction : ICommandAction
    {
        public string ActionType => "play_tts";

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new("text", PipelineActionFieldKind.Text, Required: true, Templated: true)];

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    private static CommandConfigValidator BuildValidator() =>
        new(
            [
                new FakeTemplatedAction(),
                new FakeNonTemplatedNumberAction(),
                new FakeVariableDeclaringAction(),
                new FakeTemplatedTtsAction(),
            ],
            new TemplateHelperValidator()
        );

    private static PipelineGraphInput PickThenSpeak(string ttsText, string? variable = "line")
    {
        Dictionary<string, object?> pick = Config("list", "auction-scripts");
        if (variable is not null)
            pick["variable"] = JsonSerializer.SerializeToElement(variable);

        return new PipelineGraphInput([
            new PipelineStepInput("pick_from_list", pick),
            new PipelineStepInput("play_tts", Config("text", ttsText)),
            new PipelineStepInput("send_message", Config("message", ttsText)),
        ]);
    }

    [Fact]
    public async Task A_variable_declared_by_an_earlier_step_is_valid_in_every_later_templated_field()
    {
        CommandConfigValidator sut = BuildValidator();

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(PickThenSpeak("{line}"))
        ).Value;

        result.IsValid.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task Every_variable_the_engine_sets_is_valid_without_any_declaring_step()
    {
        CommandConfigValidator sut = BuildValidator();

        string template = string.Join(
            " ",
            PipelineEngineVariables.All.Select(name => "{" + name + "}")
        );
        PipelineGraphInput graph = new([
            new PipelineStepInput("send_message", Config("message", template)),
        ]);

        PipelineValidationResult result = (await sut.ValidatePipelineAsync(graph)).Value;

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        template.Should().Contain("{last.output}").And.Contain("{last.error}");
    }

    [Fact]
    public async Task A_declaring_step_without_a_name_declares_its_default_variable()
    {
        CommandConfigValidator sut = BuildValidator();

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(PickThenSpeak("{pick}", variable: null))
        ).Value;

        result.IsValid.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task A_name_no_step_declares_is_still_rejected_as_an_unknown_helper()
    {
        CommandConfigValidator sut = BuildValidator();

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(PickThenSpeak("{lnie}"))
        ).Value;

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("UNKNOWN_TEMPLATE_HELPER");
        result.ErrorMessage.Should().Contain("lnie");
    }

    private static Dictionary<string, object?> Config(string key, string value) =>
        new() { [key] = JsonSerializer.SerializeToElement(value) };

    [Fact]
    public async Task Unknown_helper_in_a_templated_action_field_is_rejected_naming_the_key()
    {
        CommandConfigValidator sut = BuildValidator();

        PipelineGraphInput graph = new([
            new PipelineStepInput("send_message", Config("message", "Hi {user.nmae}!")),
        ]);

        Application.Common.Models.Result<PipelineValidationResult> result =
            await sut.ValidatePipelineAsync(graph);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.ErrorCode.Should().Be("UNKNOWN_TEMPLATE_HELPER");
        result.Value.ErrorMessage.Should().Contain("user.nmae");
    }

    [Fact]
    public async Task Valid_helper_in_a_templated_action_field_saves()
    {
        CommandConfigValidator sut = BuildValidator();

        PipelineGraphInput graph = new([
            new PipelineStepInput("send_message", Config("message", "Hi {user.name}!")),
        ]);

        PipelineValidationResult result = (await sut.ValidatePipelineAsync(graph)).Value;

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Brace_shaped_text_in_a_non_templated_numeric_field_is_never_validated_as_a_template()
    {
        CommandConfigValidator sut = BuildValidator();

        // "seconds" is a Number field on timeout_user, not Templated — a stray brace-shaped string here
        // must never be treated as a helper placeholder and rejected.
        PipelineGraphInput graph = new([
            new PipelineStepInput("timeout_user", Config("seconds", "{not.a.helper}")),
        ]);

        PipelineValidationResult result = (await sut.ValidatePipelineAsync(graph)).Value;

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateAction_single_action_path_also_rejects_an_unknown_helper_by_name()
    {
        CommandConfigValidator sut = BuildValidator();

        ActionDefinition action = new()
        {
            Type = "send_message",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["message"] = JsonSerializer.SerializeToElement("Hi {user.nmae}!"),
            },
        };

        PipelineValidationResult result = sut.ValidateAction(action).Value;

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("UNKNOWN_TEMPLATE_HELPER");
        result.ErrorMessage.Should().Contain("user.nmae");
    }
}
