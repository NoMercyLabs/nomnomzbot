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
/// The S-CONSEQ guard: every destructive controller action in the WHOLE Api assembly must show the user a
/// real, counted blast radius before the save — or be explicitly, reasonedly classified as one that cannot
/// have one. The scanner enumerates STRUCTURALLY (reflection over every controller, every destructive verb),
/// never from a hand-maintained list: a guard that only checks the endpoints someone remembered to list is
/// not a guard, and that is exactly how ~60 unclassified deletes survived the first slice.
/// <para>
/// Each discovered action lands in exactly one classification:
/// <list type="bullet">
/// <item><b>Covered</b> — <c>[DestructiveAction(HasCountedBlastRadius = true)]</c>: a real counted preview
/// exists and the confirm surface renders it.</item>
/// <item><b>Pending</b> — <c>[DestructiveAction(PendingBlastRadiusSince = "…")]</c>: it NEEDS a counted blast
/// radius and does not have one yet. Admitted only while named in <see cref="PendingBaseline"/>, a dated list
/// that may only shrink.</item>
/// <item><b>IntrinsicallyScoped / NotDestructive</b> — <c>[NotDestructive(reason)]</c>: it deletes a leaf row
/// nothing references, or flips a reversible state. The reason must state a real schema fact.</item>
/// </list>
/// Anything else FAILS LOUD. An unknown destructive endpoint breaks the build; it never defaults to "fine".
/// </para>
/// </summary>
public class DestructiveActionScannerTests
{
    private static readonly string[] DisableNameFragments = ["disable", "deactivate"];

    // Destructive POST actions — a destructive operation does not always get a DELETE verb. GDPR erasure is
    // the one where an uncounted blast radius is least forgivable, and it is a POST.
    private static readonly string[] DestructivePostFragments =
    [
        "erasure",
        "purge",
        "wipe",
        "shred",
    ];

    /// <summary>
    /// The dated baseline of destructive actions that NEED a counted blast radius and do not have one yet
    /// (established 2026-08-26, S-CONSEQ-c1). This list may only SHRINK: the test asserts set EQUALITY, so a
    /// newly added destructive action cannot slip in, and a fixed one must be removed from here in the same
    /// commit that covers it. Each entry is a real referencing-schema fact, not a shrug:
    /// <list type="bullet">
    /// <item>AssetsController.Delete — widgets/sound clips reference an asset by URL.</item>
    /// <item>BundlesController.Uninstall — removes the commands/widgets/pick-lists the bundle installed.</item>
    /// <item>CatalogController.DeleteItem — CatalogPurchase rows carry CatalogItemId.</item>
    /// <item>ChannelsController.DeleteChannel — nearly every tenant-scoped table carries BroadcasterId.</item>
    /// <item>CodeScriptsController.Delete — CodeScriptVersion and PipelineStep carry CodeScriptId.</item>
    /// <item>CustomDataSourcesController.Delete — pipeline steps and widgets read the source by key.</item>
    /// <item>DiscordController.Disconnect — SupporterConnection carries IntegrationConnectionId.</item>
    /// <item>EconomyLeaderboardsController.DeleteConfig — LeaderboardSnapshot carries LeaderboardConfigId.</item>
    /// <item>GiveawayCodePoolsController.Delete — GiveawayCode carries CodePoolId; Giveaway carries PrizeCodePoolId.</item>
    /// <item>GiveawaysController.Delete — GiveawayEntry and GiveawayWinner carry GiveawayId.</item>
    /// <item>IntegrationsController.Disconnect — SupporterConnection carries IntegrationConnectionId.</item>
    /// <item>PickListsController.DeletePickList — PickFromListAction resolves lists by name from pipeline steps.</item>
    /// <item>RewardsController.DeleteReward — Redemption and RedemptionTimer carry RewardId.</item>
    /// <item>SoundClipsController.Delete — pipeline steps reference the clip id inside PipelineStep.ConfigJson.</item>
    /// <item>WebhooksController.DeleteInbound — CustomDataSource and SupporterConnection carry InboundWebhookEndpointId.</item>
    /// <item>WidgetsController.DeleteWidget — WidgetVersion carries WidgetId; pipeline steps reference it in ConfigJson.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> PendingBaseline =
    [
        "AssetsController.Delete",
        "BundlesController.Uninstall",
        "CatalogController.DeleteItem",
        "ChannelsController.DeleteChannel",
        "CodeScriptsController.Delete",
        "CustomDataSourcesController.Delete",
        "DiscordController.Disconnect",
        "EconomyLeaderboardsController.DeleteConfig",
        "GiveawayCodePoolsController.Delete",
        "GiveawaysController.Delete",
        "IntegrationsController.Disconnect",
        "PickListsController.DeletePickList",
        "RewardsController.DeleteReward",
        "SoundClipsController.Delete",
        "WebhooksController.DeleteInbound",
        "WidgetsController.DeleteWidget",
    ];

    private static bool IsDestructive(MethodInfo method)
    {
        if (method.GetCustomAttribute<HttpDeleteAttribute>() is not null)
            return true;

        bool isDisable =
            (
                method.GetCustomAttribute<HttpPutAttribute>() is not null
                || method.GetCustomAttribute<HttpPatchAttribute>() is not null
            )
            && DisableNameFragments.Any(fragment =>
                method.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
            );
        if (isDisable)
            return true;

        HttpPostAttribute? post = method.GetCustomAttribute<HttpPostAttribute>();
        if (post is null)
            return false;

        string surface = $"{method.Name} {post.Template}";
        return DestructivePostFragments.Any(fragment =>
            surface.Contains(fragment, StringComparison.OrdinalIgnoreCase)
        );
    }

    private static List<MethodInfo> DiscoverDestructiveActions()
    {
        List<MethodInfo> destructiveActions = [];

        foreach (
            Type controllerType in typeof(PipelinesController)
                .Assembly.GetTypes()
                .Where(type =>
                    typeof(ControllerBase).IsAssignableFrom(type) && type is { IsAbstract: false }
                )
        )
        {
            foreach (
                MethodInfo method in controllerType.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            {
                if (IsDestructive(method))
                    destructiveActions.Add(method);
            }
        }

        return destructiveActions;
    }

    private static string Name(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    [Fact]
    public void Scanner_enumerates_the_whole_api_surface_not_a_hand_written_list()
    {
        List<MethodInfo> destructiveActions = DiscoverDestructiveActions();

        // 70 [HttpDelete] actions + the two erasure POSTs were counted on the 2026-08-26 tree. The floor
        // guards against a reflection change that silently discovers nothing (which would make every
        // assertion below pass for the wrong reason); it is deliberately below the real count so ADDING an
        // endpoint never fails here — an added endpoint fails the classification test instead, by name.
        Assert.True(
            destructiveActions.Count >= 70,
            $"Structural scan found only {destructiveActions.Count} destructive actions; the Api assembly has 70+."
        );

        // Spot-check the structural reach: endpoints in controllers no hand-written list ever named.
        List<string> names = destructiveActions.Select(Name).ToList();
        Assert.Contains("GdprController.RequestErasure", names);
        Assert.Contains("ComplianceController.RequestErasure", names);
        Assert.Contains("ModerationController.DeleteUserNote", names);
        Assert.Contains("WebhooksController.DeleteOutbound", names);
        Assert.Contains("ChannelsController.DeleteChannel", names);
    }

    [Fact]
    public void Every_discovered_destructive_action_is_explicitly_classified()
    {
        List<string> unclassified = [];
        foreach (MethodInfo action in DiscoverDestructiveActions())
        {
            DestructiveActionAttribute? destructive =
                action.GetCustomAttribute<DestructiveActionAttribute>();
            bool covered = destructive?.HasCountedBlastRadius is true;
            bool pending = !string.IsNullOrWhiteSpace(destructive?.PendingBlastRadiusSince);
            bool exempt = action.GetCustomAttribute<NotDestructiveAttribute>() is not null;

            if (!covered && !pending && !exempt)
                unclassified.Add(Name(action));
        }

        Assert.True(
            unclassified.Count == 0,
            "Destructive action(s) with no S-CONSEQ classification — add a counted blast radius, a dated "
                + "PendingBlastRadiusSince baseline entry, or a reasoned [NotDestructive]: "
                + string.Join(", ", unclassified)
        );
    }

    [Fact]
    public void Pending_blast_radius_actions_match_the_dated_baseline_exactly()
    {
        HashSet<string> pending =
        [
            .. DiscoverDestructiveActions()
                .Where(action =>
                    !string.IsNullOrWhiteSpace(
                        action
                            .GetCustomAttribute<DestructiveActionAttribute>()
                            ?.PendingBlastRadiusSince
                    )
                )
                .Select(Name),
        ];

        List<string> added = pending.Except(PendingBaseline).Order().ToList();
        List<string> fixedSince = PendingBaseline.Except(pending).Order().ToList();

        Assert.True(
            added.Count == 0,
            $"New destructive action(s) shipped without a counted blast radius: {string.Join(", ", added)}. "
                + "The baseline may only shrink — give them a counted preview instead."
        );
        Assert.True(
            fixedSince.Count == 0,
            $"Baseline entries no longer pending: {string.Join(", ", fixedSince)}. Remove them from "
                + "PendingBaseline in the same commit that covers them."
        );
    }

    [Fact]
    public void A_classification_is_exactly_one_state_never_two()
    {
        List<string> ambiguous = [];
        foreach (MethodInfo action in DiscoverDestructiveActions())
        {
            DestructiveActionAttribute? destructive =
                action.GetCustomAttribute<DestructiveActionAttribute>();
            bool covered = destructive?.HasCountedBlastRadius is true;
            bool pending = !string.IsNullOrWhiteSpace(destructive?.PendingBlastRadiusSince);
            bool exempt = action.GetCustomAttribute<NotDestructiveAttribute>() is not null;

            int states = (covered ? 1 : 0) + (pending ? 1 : 0) + (exempt ? 1 : 0);
            if (states > 1)
                ambiguous.Add(Name(action));
        }

        Assert.True(
            ambiguous.Count == 0,
            $"Destructive action(s) carrying more than one classification: {string.Join(", ", ambiguous)}"
        );
    }

    [Fact]
    public void Exemptions_state_a_reason_never_an_empty_shrug()
    {
        List<string> empty = [];
        foreach (MethodInfo action in DiscoverDestructiveActions())
        {
            NotDestructiveAttribute? exemption =
                action.GetCustomAttribute<NotDestructiveAttribute>();
            if (exemption is not null && exemption.Reason.Trim().Length < 20)
                empty.Add(Name(action));
        }

        Assert.True(
            empty.Count == 0,
            $"[NotDestructive] without a real stated reason: {string.Join(", ", empty)}"
        );
    }

    [Fact]
    public void Gdpr_erasure_carries_a_counted_blast_radius_not_an_exemption()
    {
        MethodInfo requestErasure = typeof(GdprController).GetMethod(
            nameof(GdprController.RequestErasure)
        )!;

        DestructiveActionAttribute? attribute =
            requestErasure.GetCustomAttribute<DestructiveActionAttribute>();

        Assert.NotNull(attribute);
        Assert.True(attribute.HasCountedBlastRadius);
        Assert.Null(attribute.PendingBlastRadiusSince);
        Assert.Null(requestErasure.GetCustomAttribute<NotDestructiveAttribute>());

        // …and the counted preview it promises actually exists on the same controller.
        Assert.NotNull(typeof(GdprController).GetMethod(nameof(GdprController.PreviewErasure)));
        Assert.NotNull(
            typeof(ComplianceController).GetMethod(nameof(ComplianceController.PreviewErasure))
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
        Assert.True(attribute.HasCountedBlastRadius);
        Assert.Null(deletePipeline.GetCustomAttribute<NotDestructiveAttribute>());
    }
}
