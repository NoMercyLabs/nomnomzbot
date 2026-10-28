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

using System.Text.RegularExpressions;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Stage;

/// <summary>
/// Pipeline action <c>stage_effect</c>: plays one one-off effect (run across the screen, confetti, shake, flash) on
/// the channel's stage widget. Fail-closed like <c>widget_event</c>: the stage widget must exist, be enabled and be
/// open in a browser source, otherwise a typed failure and no push. The preset comes from
/// <see cref="StagePresetRegistry"/>; the payload carries only the fields that preset reads. The text is already
/// template-resolved by the engine and travels as a plain string; the widget sets it through a text node.
/// </summary>
public sealed partial class StageEffectAction : ICommandAction
{
    private const int MaxDurationMs = 30_000;
    private const int MaxIntensity = 10;
    private const int MaxTextLength = 120;

    private readonly IWidgetService _widgets;
    private readonly IWidgetEventNotifier _overlay;

    public StageEffectAction(IWidgetService widgets, IWidgetEventNotifier overlay)
    {
        _widgets = widgets;
        _overlay = overlay;
    }

    public string ActionType => "stage_effect";

    public LocalizedText Category => new("pipeline.category.widgets");

    public LocalizedText Description => new("pipeline.stage_effect.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "preset",
                PipelineActionFieldKind.Enum,
                Required: true,
                Options: StagePresetRegistry.Ids,
                Description: new("pipeline.stage_effect.preset.help")
            ),
            new(
                "text",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.stage_effect.text.help")
            ),
            new(
                "duration_ms",
                PipelineActionFieldKind.Number,
                Description: new("pipeline.stage_effect.duration_ms.help")
            ),
            new(
                "intensity",
                PipelineActionFieldKind.Number,
                Description: new("pipeline.stage_effect.intensity.help")
            ),
            new(
                "color",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.stage_effect.color.help")
            ),
        ];

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? presetId = action.GetString("preset");
        IStagePreset? preset = StagePresetRegistry.Find(presetId);
        if (preset is null)
            return ActionResult.Failure(
                $"stage_effect: unknown preset '{presetId}'. Use one of: {string.Join(", ", StagePresetRegistry.Ids)}.",
                "STAGE_UNKNOWN_PRESET"
            );

        string? color = action.GetString("color")?.Trim();
        if (!string.IsNullOrEmpty(color) && !HexColor().IsMatch(color))
            return ActionResult.Failure(
                "stage_effect: 'color' must be a hex color such as #ff8800.",
                "STAGE_INVALID_COLOR"
            );

        Result<WidgetDetail> widget = await _widgets.FindInstalledAsync(
            ctx.BroadcasterId.ToString(),
            StageSurface.WidgetKey,
            ctx.CancellationToken
        );
        if (!widget.IsSuccess)
            return ActionResult.Failure(
                "stage_effect: this channel has no Stage widget.",
                "STAGE_WIDGET_MISSING"
            );
        if (!widget.Value.IsEnabled)
            return ActionResult.Failure(
                "stage_effect: the Stage widget is disabled.",
                "STAGE_WIDGET_DISABLED"
            );
        if (!widget.Value.IsAttached)
            return ActionResult.Failure(
                "stage_effect: no browser source has the Stage widget open, so nobody would see the effect. Add the Stage browser source in OBS.",
                "STAGE_WIDGET_NOT_ATTACHED"
            );

        StageEffectPayload payload = BuildPayload(preset, action, color);
        await _overlay.SendWidgetEventAsync(
            ctx.BroadcasterId,
            widget.Value.Id,
            StageSurface.EventType,
            payload,
            ctx.CancellationToken
        );

        return ActionResult.Success($"stage_effect:{preset.Id}");
    }

    // Only the fields the preset reads are sent; a missing duration runs the preset default, and a missing
    // intensity or color stays null so the widget falls back to its own channel settings.
    private static StageEffectPayload BuildPayload(
        IStagePreset preset,
        ActionDefinition action,
        string? color
    )
    {
        int durationMs = action.GetInt("duration_ms");
        int intensity = action.GetInt("intensity");
        string? text = action.GetString("text")?.Trim();

        return new(
            preset.Id,
            preset.Uses.HasFlag(StagePresetFields.Text) && !string.IsNullOrEmpty(text)
                ? Truncate(text)
                : null,
            durationMs > 0 ? Math.Min(durationMs, MaxDurationMs) : preset.DefaultDurationMs,
            preset.Uses.HasFlag(StagePresetFields.Intensity) && intensity > 0
                ? Math.Min(intensity, MaxIntensity)
                : null,
            preset.Uses.HasFlag(StagePresetFields.Color) && !string.IsNullOrEmpty(color)
                ? color
                : null
        );
    }

    // Never cuts an emoji in half: a cut that would end on the first half of a surrogate pair drops that half too.
    private static string Truncate(string text)
    {
        if (text.Length <= MaxTextLength)
            return text;
        int end = char.IsHighSurrogate(text[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
        return text[..end];
    }

    [GeneratedRegex("^#[0-9a-fA-F]{3,8}$")]
    private static partial Regex HexColor();
}
