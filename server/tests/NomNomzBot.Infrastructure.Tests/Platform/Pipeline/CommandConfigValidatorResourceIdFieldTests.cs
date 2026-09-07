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
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// Closes the run_code bug class at save time: a ResourceId/Widget/SoundClip/Asset field's value is whatever
/// id form the API last served the dashboard's picker in (a 26-char ULID, UlidGuidJsonConverter) — this
/// rejects anything that decodes as neither a ULID nor a raw Guid, and normalizes an accepted ULID down to
/// its canonical Guid string before <see cref="NomNomzBot.Infrastructure.Commands.PipelineService"/> persists
/// it, so <c>RunCodeAction</c> and its siblings never see the wire form at execution time.
/// </summary>
public sealed class CommandConfigValidatorResourceIdFieldTests
{
    private static readonly Guid ScriptId = Guid.Parse("0192a000-0000-7000-8000-00000000c0aa");

    private sealed class FakeResourceIdAction : ICommandAction
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

    /// <summary>A minimal action carrying exactly one field of the given kind — used to prove every
    /// owned-id-shaped <see cref="PipelineActionFieldKind"/> (not only <see cref="PipelineActionFieldKind.ResourceId"/>)
    /// gets the same ULID tolerance, since the dashboard's Widget/SoundClip/Asset pickers return the same
    /// wire-form id as the ResourceId fallback kind.</summary>
    private sealed class FakeOwnedIdKindAction(
        string actionType,
        string fieldName,
        PipelineActionFieldKind kind
    ) : ICommandAction
    {
        public string ActionType => actionType;

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new(fieldName, kind, Required: true)];

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    private sealed class FakeWidgetAction : ICommandAction
    {
        public string ActionType => "widget_event";

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new("widget_id", PipelineActionFieldKind.Widget, Required: true)];

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    private static CommandConfigValidator BuildValidator() =>
        new(
            [
                new FakeResourceIdAction(),
                new FakeWidgetAction(),
                new FakeOwnedIdKindAction(
                    "play_sound_fixture",
                    "clip_id",
                    PipelineActionFieldKind.SoundClip
                ),
                new FakeOwnedIdKindAction(
                    "asset_fixture",
                    "asset_id",
                    PipelineActionFieldKind.Asset
                ),
            ],
            new TemplateHelperValidator()
        );

    private static Dictionary<string, object?> Config(string key, string value) =>
        new() { [key] = JsonSerializer.SerializeToElement(value) };

    [Fact]
    public async Task A_ulid_form_resource_id_saves()
    {
        CommandConfigValidator sut = BuildValidator();
        string ulid = OwnedIdCodec.Encode(ScriptId);

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(
                new([new PipelineStepInput("run_code", Config("code_script_id", ulid))])
            )
        ).Value;

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_raw_guid_resource_id_still_saves()
    {
        CommandConfigValidator sut = BuildValidator();

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(
                new([
                    new PipelineStepInput(
                        "run_code",
                        Config("code_script_id", ScriptId.ToString())
                    ),
                ])
            )
        ).Value;

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_malformed_resource_id_is_rejected_naming_the_field()
    {
        CommandConfigValidator sut = BuildValidator();

        Application.Common.Models.Result<PipelineValidationResult> result =
            await sut.ValidatePipelineAsync(
                new([
                    new PipelineStepInput(
                        "run_code",
                        Config("code_script_id", "definitely-not-an-id")
                    ),
                ])
            );

        result.Value.IsValid.Should().BeFalse();
        result.Value.ErrorCode.Should().Be("INVALID_RESOURCE_ID");
        result.Value.ErrorMessage.Should().Contain("code_script_id");
    }

    [Fact]
    public async Task A_widget_kind_field_gets_the_same_ulid_tolerance_as_resource_id()
    {
        CommandConfigValidator sut = BuildValidator();
        string ulid = OwnedIdCodec.Encode(ScriptId);

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(
                new([new PipelineStepInput("widget_event", Config("widget_id", ulid))])
            )
        ).Value;

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_sound_clip_kind_field_gets_the_same_ulid_tolerance_as_resource_id()
    {
        CommandConfigValidator sut = BuildValidator();
        string ulid = OwnedIdCodec.Encode(ScriptId);

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(
                new([new PipelineStepInput("play_sound_fixture", Config("clip_id", ulid))])
            )
        ).Value;

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task An_asset_kind_field_gets_the_same_ulid_tolerance_as_resource_id()
    {
        CommandConfigValidator sut = BuildValidator();
        string ulid = OwnedIdCodec.Encode(ScriptId);

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(
                new([new PipelineStepInput("asset_fixture", Config("asset_id", ulid))])
            )
        ).Value;

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_malformed_widget_kind_field_is_rejected_just_like_resource_id()
    {
        CommandConfigValidator sut = BuildValidator();

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(
                new([new PipelineStepInput("widget_event", Config("widget_id", "garbage"))])
            )
        ).Value;

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_RESOURCE_ID");
    }

    [Fact]
    public async Task A_malformed_asset_kind_field_is_rejected_just_like_resource_id()
    {
        CommandConfigValidator sut = BuildValidator();

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(
                new([new PipelineStepInput("asset_fixture", Config("asset_id", "garbage"))])
            )
        ).Value;

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_RESOURCE_ID");
    }

    [Fact]
    public async Task A_malformed_sound_clip_kind_field_is_rejected_just_like_resource_id()
    {
        CommandConfigValidator sut = BuildValidator();

        PipelineValidationResult result = (
            await sut.ValidatePipelineAsync(
                new([new PipelineStepInput("play_sound_fixture", Config("clip_id", "garbage"))])
            )
        ).Value;

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateAction_single_action_path_also_rejects_a_malformed_resource_id()
    {
        CommandConfigValidator sut = BuildValidator();

        ActionDefinition action = new()
        {
            Type = "run_code",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["code_script_id"] = JsonSerializer.SerializeToElement("garbage"),
            },
        };

        PipelineValidationResult result = sut.ValidateAction(action).Value;

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_RESOURCE_ID");
    }

    [Fact]
    public void NormalizeResourceIdFields_decodes_a_ulid_down_to_its_canonical_guid_string()
    {
        CommandConfigValidator sut = BuildValidator();
        string ulid = OwnedIdCodec.Encode(ScriptId);
        ActionDefinition action = new()
        {
            Type = "run_code",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["code_script_id"] = JsonSerializer.SerializeToElement(ulid),
            },
        };

        ActionDefinition normalized = sut.NormalizeResourceIdFields(action);

        normalized.GetString("code_script_id").Should().Be(ScriptId.ToString());
    }

    [Fact]
    public void NormalizeResourceIdFields_leaves_an_already_canonical_guid_untouched()
    {
        CommandConfigValidator sut = BuildValidator();
        ActionDefinition action = new()
        {
            Type = "run_code",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["code_script_id"] = JsonSerializer.SerializeToElement(ScriptId.ToString()),
            },
        };

        ActionDefinition normalized = sut.NormalizeResourceIdFields(action);

        normalized.GetString("code_script_id").Should().Be(ScriptId.ToString());
    }

    [Fact]
    public void NormalizeResourceIdFields_ignores_actions_with_no_resource_id_fields()
    {
        CommandConfigValidator sut = new(
            [new FakeResourceIdAction()],
            new TemplateHelperValidator()
        );
        ActionDefinition action = new()
        {
            Type = "unknown_action",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["whatever"] = JsonSerializer.SerializeToElement("value"),
            },
        };

        ActionDefinition normalized = sut.NormalizeResourceIdFields(action);

        normalized.Should().BeSameAs(action);
    }
}
