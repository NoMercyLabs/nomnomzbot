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

using System.Reflection;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PickerSources;

/// <summary>
/// Every action field that names a thing the bot already has a list for declares a picker kind, so the step form
/// offers that list instead of a free-text box. The fields that carry a template are deliberately absent: a
/// templated field keeps its placeholder autocomplete.
/// </summary>
public sealed class PickerFieldKindsTests
{
    private static readonly Dictionary<string, ICommandAction> ActionsByType = RegisteredActions();

    private static Dictionary<string, ICommandAction> RegisteredActions()
    {
        Dictionary<string, ICommandAction> actions = new(StringComparer.Ordinal);
        foreach (
            Type type in typeof(CommandConfigValidator)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && t.IsAssignableTo(typeof(ICommandAction))
                )
        )
        {
            ConstructorInfo ctor = type.GetConstructors()
                .OrderBy(c => c.GetParameters().Length)
                .First();
            object?[] args =
            [
                .. ctor.GetParameters()
                    .Select(p =>
                        p.ParameterType.IsInterface ? Substitute.For([p.ParameterType], []) : null
                    ),
            ];
            ICommandAction action = (ICommandAction)ctor.Invoke(args);
            actions[action.ActionType] = action;
        }

        return actions;
    }

    public static TheoryData<string, string, PipelineActionFieldKind> PickerFields =>
        new()
        {
            { "vts_load_model", "model", PipelineActionFieldKind.VtsModel },
            { "vts_trigger_hotkey", "hotkey", PipelineActionFieldKind.VtsHotkey },
            { "vts_set_expression", "expression", PipelineActionFieldKind.VtsExpression },
            { "play_game", "game_type", PipelineActionFieldKind.GameType },
            { "start_live_game", "game_type", PipelineActionFieldKind.LiveGameType },
            { "set_pronoun", "pronoun", PipelineActionFieldKind.Pronoun },
            { "music_transfer_device", "deviceId", PipelineActionFieldKind.MusicDevice },
            { "play_track_once", "track_uri", PipelineActionFieldKind.MusicTrack },
            { "schedule_pipeline", "pipeline", PipelineActionFieldKind.PipelineName },
            { "schedule_pipeline", "command", PipelineActionFieldKind.CommandName },
            { "schedule_pipeline", "action_type", PipelineActionFieldKind.ActionType },
            { "run_pipeline", "pipeline", PipelineActionFieldKind.Pipeline },
            { "jar_contribute", "jar_id", PipelineActionFieldKind.SavingsJar },
            { "open_giveaway", "giveaway_id", PipelineActionFieldKind.Giveaway },
            { "draw_giveaway", "giveaway_id", PipelineActionFieldKind.Giveaway },
            { "enter_giveaway", "giveaway_id", PipelineActionFieldKind.Giveaway },
            { "post_quote", "number", PipelineActionFieldKind.Quote },
            { "send_webhook", "endpoint", PipelineActionFieldKind.WebhookEndpoint },
        };

    [Theory]
    [MemberData(nameof(PickerFields))]
    public void The_field_declares_its_picker_kind(
        string actionType,
        string field,
        PipelineActionFieldKind kind
    )
    {
        PipelineActionFieldDescriptor descriptor = ActionsByType[actionType]
            .Fields.Single(f => f.Name == field);

        Assert.Equal(kind, descriptor.Kind);
        Assert.False(descriptor.Templated);
    }

    [Fact]
    public void A_new_picker_kind_never_sits_on_a_templated_field()
    {
        HashSet<PipelineActionFieldKind> newKinds =
        [
            .. PickerFields.Select(r => (PipelineActionFieldKind)r[2]),
        ];
        List<string> templatedPickers =
        [
            .. ActionsByType.Values.SelectMany(a =>
                a.Fields.Where(f => f.Templated && newKinds.Contains(f.Kind))
                    .Select(f => $"{a.ActionType}.{f.Name}")
            ),
        ];

        Assert.Empty(templatedPickers);
    }
}
