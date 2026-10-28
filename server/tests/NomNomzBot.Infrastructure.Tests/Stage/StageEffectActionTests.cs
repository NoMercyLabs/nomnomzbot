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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Stage;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stage;

/// <summary>
/// Behavior of the <c>stage_effect</c> pipeline action: it pushes exactly one <c>stage_effect</c> widget event to
/// the channel's stage widget with the resolved payload, and it fails closed (a typed failure, zero pushes) when
/// the stage widget is missing, disabled or not open in a browser source, or when the preset is unknown.
/// </summary>
public sealed class StageEffectActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid StageWidgetId = Guid.Parse("0192a000-0000-7000-8000-0000000000aa");

    private readonly IWidgetService _widgets = Substitute.For<IWidgetService>();
    private readonly IWidgetEventNotifier _overlay = Substitute.For<IWidgetEventNotifier>();
    private readonly List<(Guid WidgetId, string EventType, object? Data)> _pushes = [];

    public StageEffectActionTests()
    {
        _overlay
            .SendWidgetEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                _pushes.Add((call.ArgAt<Guid>(1), call.ArgAt<string>(2), call.ArgAt<object?>(3)));
                return Task.CompletedTask;
            });
    }

    private StageEffectAction Action() => new(_widgets, _overlay);

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = "user",
            TriggeredByDisplayName = "User",
            MessageId = "msg",
            RawMessage = "",
        };

    private static ActionDefinition Def(string json) =>
        JsonSerializer.Deserialize<ActionDefinition>(json)!;

    private void StageWidgetIs(bool enabled = true, bool attached = true) =>
        _widgets
            .FindInstalledAsync(Broadcaster.ToString(), "stage", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new WidgetDetail(
                        StageWidgetId,
                        "Stage",
                        null,
                        "vue",
                        "<template/>",
                        enabled,
                        null,
                        null,
                        null,
                        new(),
                        [],
                        null,
                        null,
                        DateTime.UtcNow,
                        DateTime.UtcNow,
                        false,
                        attached,
                        false
                    )
                )
            );

    private void NoStageWidget() =>
        _widgets
            .FindInstalledAsync(Broadcaster.ToString(), "stage", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<WidgetDetail>("not installed", "NOT_FOUND"));

    private StageEffectPayload OnlyPush()
    {
        _pushes.Should().ContainSingle();
        _pushes[0].WidgetId.Should().Be(StageWidgetId);
        _pushes[0].EventType.Should().Be("stage_effect");
        return _pushes[0].Data.Should().BeOfType<StageEffectPayload>().Subject;
    }

    [Fact]
    public async Task A_templated_run_across_pushes_one_effect_with_the_resolved_payload_to_the_stage_widget()
    {
        StageWidgetIs();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);
        PipelineEngine engine = new(
            Substitute.For<IApplicationDbContext>(),
            registry,
            [Action()],
            [],
            TestTemplateResolver.Create(),
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System
        );

        PipelineExecutionResult result = await engine.ExecuteAsync(
            new PipelineRequest
            {
                BroadcasterId = Broadcaster,
                TriggeredByUserId = "u1",
                TriggeredByDisplayName = "User",
                PipelineJson = /*lang=json*/
                """
                {"steps":[{"action":{"type":"stage_effect","preset":"run_across","text":"{user} rides the train","duration_ms":4500,"intensity":7,"color":"#ff8800"}}]}
                """,
                MessageId = "m1",
                RawMessage = "",
                InitialVariables = new Dictionary<string, string> { ["user"] = "kitte" },
            }
        );

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        OnlyPush()
            .Should()
            .Be(new StageEffectPayload("run_across", "kitte rides the train", 4500, 7, "#ff8800"));
    }

    [Fact]
    public async Task A_viewers_markup_in_the_text_reaches_the_widget_as_the_same_plain_string()
    {
        StageWidgetIs();
        const string Markup = "<img src=x onerror=alert(1)><script>steal()</script>";

        string json =
            "{\"type\":\"stage_effect\",\"preset\":\"run_across\",\"text\":"
            + JsonSerializer.Serialize(Markup)
            + "}";

        ActionResult result = await Action().ExecuteAsync(Ctx(), Def(json));

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        StageEffectPayload payload = OnlyPush();
        payload.Text.Should().Be(Markup);
    }

    [Fact]
    public async Task An_effect_without_duration_or_intensity_runs_the_preset_default_and_leaves_intensity_to_the_widget()
    {
        StageWidgetIs();

        ActionResult result = await Action()
            .ExecuteAsync(Ctx(), Def("""{"type":"stage_effect","preset":"confetti"}"""));

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        OnlyPush().Should().Be(new StageEffectPayload("confetti", null, 4000, null, null));
    }

    [Fact]
    public async Task A_field_the_preset_does_not_use_is_not_sent()
    {
        StageWidgetIs();

        ActionResult result = await Action()
            .ExecuteAsync(
                Ctx(),
                Def(
                    """{"type":"stage_effect","preset":"shake","text":"hi","color":"#ffffff","intensity":3}"""
                )
            );

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        OnlyPush().Should().Be(new StageEffectPayload("shake", null, 1500, 3, null));
    }

    [Fact]
    public async Task Duration_and_intensity_are_clamped_to_the_limits()
    {
        StageWidgetIs();

        ActionResult result = await Action()
            .ExecuteAsync(
                Ctx(),
                Def(
                    """{"type":"stage_effect","preset":"flash","duration_ms":999999,"intensity":99}"""
                )
            );

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        StageEffectPayload payload = OnlyPush();
        payload.DurationMs.Should().Be(30_000);
        payload.Intensity.Should().Be(10);
    }

    [Theory]
    [InlineData("none", "STAGE_WIDGET_MISSING")]
    [InlineData("disabled", "STAGE_WIDGET_DISABLED")]
    [InlineData("detached", "STAGE_WIDGET_NOT_ATTACHED")]
    public async Task A_stage_widget_that_cannot_show_the_effect_fails_closed_with_a_typed_failure_and_no_push(
        string state,
        string expectedCode
    )
    {
        switch (state)
        {
            case "none":
                NoStageWidget();
                break;
            case "disabled":
                StageWidgetIs(enabled: false);
                break;
            default:
                StageWidgetIs(attached: false);
                break;
        }

        ActionResult result = await Action()
            .ExecuteAsync(Ctx(), Def("""{"type":"stage_effect","preset":"flash"}"""));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(expectedCode);
        result.ErrorMessage.Should().Contain("stage");
        _pushes.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_preset_fails_closed_and_names_the_presets_that_exist()
    {
        StageWidgetIs();

        ActionResult result = await Action()
            .ExecuteAsync(Ctx(), Def("""{"type":"stage_effect","preset":"explode"}"""));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("STAGE_UNKNOWN_PRESET");
        result
            .ErrorMessage.Should()
            .Contain("explode")
            .And.Contain("confetti")
            .And.Contain("flash")
            .And.Contain("run_across")
            .And.Contain("shake");
        _pushes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_color_that_is_not_a_hex_value_fails_closed_before_it_can_reach_a_style()
    {
        StageWidgetIs();

        ActionResult result = await Action()
            .ExecuteAsync(
                Ctx(),
                Def(
                    """{"type":"stage_effect","preset":"flash","color":"red;background:url(//evil.example/x)"}"""
                )
            );

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("STAGE_INVALID_COLOR");
        _pushes.Should().BeEmpty();
    }
}
