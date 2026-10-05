// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// The save-time validator demands a ULID/Guid for every field of an owned-id kind
/// (<see cref="PipelineActionFieldKind.ResourceId"/>, Widget, SoundClip, Asset). An action whose
/// <c>ExecuteAsync</c> reads such a field as a NAME, a URI or an external id (a pick-list name, an OBS scene,
/// a Spotify playlist) can therefore never be saved. This sweep walks every registered
/// <see cref="ICommandAction"/> and pins the closed list of fields that really are owned ids.
/// </summary>
public sealed class OwnedIdFieldKindSweepTests
{
    // Fields whose ExecuteAsync decodes the value with OwnedIdCodec / Guid.TryParse.
    private static readonly HashSet<string> TrueOwnedIdFields = new(StringComparer.Ordinal)
    {
        "run_code.code_script_id",
        "jar_contribute.jar_id",
        "enter_giveaway.giveaway_id",
        "draw_giveaway.giveaway_id",
        "open_giveaway.giveaway_id",
        "run_pipeline.pipeline",
        "send_webhook.endpoint",
    };

    private static List<ICommandAction> AllRegisteredActions()
    {
        Assembly infrastructure = typeof(CommandConfigValidator).Assembly;
        List<ICommandAction> actions = [];
        foreach (
            Type type in infrastructure
                .GetTypes()
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
            actions.Add((ICommandAction)ctor.Invoke(args));
        }

        return actions;
    }

    [Fact]
    public void Only_fields_that_execute_as_owned_ids_declare_an_owned_id_kind()
    {
        List<string> ownedIdFields =
        [
            .. AllRegisteredActions()
                .SelectMany(a =>
                    a.Fields.Where(f => f.Kind == PipelineActionFieldKind.ResourceId)
                        .Select(f => $"{a.ActionType}.{f.Name}")
                ),
        ];

        ownedIdFields.Should().BeSubsetOf(TrueOwnedIdFields);
    }

    [Fact]
    public void The_sweep_actually_found_the_registered_actions()
    {
        AllRegisteredActions().Should().Contain(a => a.ActionType == "pick_from_list");
        AllRegisteredActions().Count.Should().BeGreaterThan(40);
    }
}
