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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Infrastructure.Obs.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs.PipelineActions;

/// <summary>
/// Every OBS action field that names a thing inside OBS declares a picker kind, so the step form offers a list from
/// the connected OBS instead of a free-text box. A field added later that names an OBS thing as plain text fails the
/// last test here.
/// </summary>
public sealed class ObsNameFieldKindsTests
{
    private static readonly IObsControlService Obs = Substitute.For<IObsControlService>();

    private static ICommandAction[] Actions =>
        [
            new ObsSwitchSceneAction(Obs),
            new ObsSetPreviewSceneAction(Obs),
            new ObsSetSourceAction(Obs),
            new ObsFilterAction(Obs),
            new ObsTransitionAction(Obs),
            new ObsInputMuteAction(Obs),
            new ObsInputVolumeAction(Obs),
            new ObsMediaAction(Obs),
            new ObsHotkeyAction(Obs),
            new ObsRefreshBrowserAction(Obs),
            new ObsScreenshotAction(Obs),
        ];

    public static TheoryData<string, string, PipelineActionFieldKind> NameFields =>
        new()
        {
            { "obs_switch_scene", "scene", PipelineActionFieldKind.ObsScene },
            { "obs_set_preview_scene", "scene", PipelineActionFieldKind.ObsScene },
            { "obs_set_source", "scene", PipelineActionFieldKind.ObsScene },
            { "obs_set_source", "source", PipelineActionFieldKind.ObsSceneItem },
            { "obs_filter", "source", PipelineActionFieldKind.ObsSceneItem },
            { "obs_filter", "filter", PipelineActionFieldKind.ObsFilter },
            { "obs_screenshot", "source", PipelineActionFieldKind.ObsSceneItem },
            { "obs_transition", "transition", PipelineActionFieldKind.ObsTransition },
            { "obs_input_mute", "input", PipelineActionFieldKind.ObsAudioInput },
            { "obs_input_volume", "input", PipelineActionFieldKind.ObsAudioInput },
            { "obs_media", "input", PipelineActionFieldKind.ObsInput },
            { "obs_refresh_browser", "input", PipelineActionFieldKind.ObsInput },
            { "obs_hotkey", "hotkey_name", PipelineActionFieldKind.ObsHotkey },
        };

    [Theory]
    [MemberData(nameof(NameFields))]
    public void An_obs_name_field_declares_its_picker_kind(
        string actionType,
        string field,
        PipelineActionFieldKind expected
    )
    {
        ICommandAction action = Assert.Single(Actions, a => a.ActionType == actionType);

        PipelineActionFieldDescriptor descriptor = Assert.Single(
            action.Fields,
            f => f.Name == field
        );

        Assert.Equal(expected, descriptor.Kind);
    }

    [Fact]
    public void The_table_covers_every_obs_name_field_that_exists()
    {
        HashSet<string> declared = [.. NameFields.Select(row => $"{row[0]}.{row[1]}")];
        string[] nameWords = ["scene", "source", "filter", "transition", "input", "hotkey_name"];

        List<string> plainTextNames =
        [
            .. Actions
                .SelectMany(a => a.Fields.Select(f => (a.ActionType, Field: f)))
                .Where(x => nameWords.Contains(x.Field.Name))
                .Select(x => $"{x.ActionType}.{x.Field.Name}"),
        ];

        Assert.Equal(13, declared.Count);
        Assert.Equal(declared.Order(), plainTextNames.Order());
    }
}
