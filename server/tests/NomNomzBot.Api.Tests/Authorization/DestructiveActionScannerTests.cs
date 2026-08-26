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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// S-CONSEQ guard, SCOPED to this slice's target surface (commands-pipelines-pinch-points.md):
/// deleting a pipeline other things reference, deleting/disabling a command, deleting a sound clip,
/// deleting a widget — the highest-regret destructive actions a streamer can take. It is
/// deliberately NOT a repo-wide scan: 60+ other `[HttpDelete]` actions exist across the ~87
/// controllers (auth revocations, GDPR withdrawal, moderation unbans, webhooks, music queue removal,
/// …) and classifying all of them is out of this slice's blast radius — see the slice report for the
/// honest N-of-M. Within its scope, it structurally enumerates every action that is destructive by
/// HTTP verb (DELETE, or PUT/PATCH whose name reads as a disable) and requires each one to be
/// explicitly classified — either <see cref="DestructiveActionAttribute"/> with a counted blast
/// radius, or a reasoned <see cref="NotDestructiveAttribute"/> exemption. An action the scanner finds
/// but cannot classify FAILS LOUD, by design: a guard that silently skips what it does not understand
/// is the defect this project has paid for repeatedly.
/// </summary>
public class DestructiveActionScannerTests
{
    private static readonly string[] DisableNameFragments = ["disable", "deactivate"];

    /// <summary>
    /// The controllers this slice's blast-radius mechanism was applied (or explicitly exempted)
    /// against. Widening this set is the natural next slice, not silent scope creep here.
    /// </summary>
    private static readonly Type[] ScannedControllers =
    [
        typeof(PipelinesController),
        typeof(CommandsController),
        typeof(SoundClipsController),
        typeof(WidgetsController),
    ];

    private static List<MethodInfo> DiscoverDestructiveActions()
    {
        List<MethodInfo> destructiveActions = [];

        foreach (Type controllerType in ScannedControllers)
        {
            foreach (
                MethodInfo method in controllerType.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            {
                bool isDelete = method.GetCustomAttribute<HttpDeleteAttribute>() is not null;
                bool isDisable =
                    (
                        method.GetCustomAttribute<HttpPutAttribute>() is not null
                        || method.GetCustomAttribute<HttpPatchAttribute>() is not null
                    )
                    && DisableNameFragments.Any(fragment =>
                        method.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                    );

                if (isDelete || isDisable)
                    destructiveActions.Add(method);
            }
        }

        return destructiveActions;
    }

    [Fact]
    public void Every_discovered_destructive_action_is_explicitly_classified()
    {
        List<MethodInfo> destructiveActions = DiscoverDestructiveActions();

        // Sanity: the scanner must actually find the endpoints this slice covers, or it is
        // silently discovering nothing and every assertion below would pass for the wrong reason.
        Assert.True(
            destructiveActions.Count >= 4,
            $"Expected to discover at least the 4 target destructive actions (pipeline/command/sound-clip/widget delete); found {destructiveActions.Count}."
        );

        List<string> unclassified = [];
        foreach (MethodInfo action in destructiveActions)
        {
            bool hasCoveredBlastRadius =
                action.GetCustomAttribute<DestructiveActionAttribute>()?.HasCountedBlastRadius
                is true;
            bool hasExemption = action.GetCustomAttribute<NotDestructiveAttribute>() is not null;

            if (!hasCoveredBlastRadius && !hasExemption)
                unclassified.Add($"{action.DeclaringType!.Name}.{action.Name}");
        }

        Assert.True(
            unclassified.Count == 0,
            "Destructive action(s) shipped without a counted blast radius AND without a reasoned "
                + $"exemption: {string.Join(", ", unclassified)}"
        );
    }

    [Fact]
    public void Pipeline_delete_carries_a_counted_blast_radius_not_an_exemption()
    {
        MethodInfo deletePipeline = typeof(PipelinesController).GetMethod(
            nameof(PipelinesController.DeletePipeline)
        )!;

        DestructiveActionAttribute? attribute =
            deletePipeline.GetCustomAttribute<DestructiveActionAttribute>();

        Assert.NotNull(attribute);
        Assert.True(attribute!.HasCountedBlastRadius);
        Assert.Null(deletePipeline.GetCustomAttribute<NotDestructiveAttribute>());
    }
}
