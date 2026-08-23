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
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Api.RateLimiting;
using Xunit;

namespace NomNomzBot.Api.Tests.RateLimiting;

/// <summary>
/// S118 — finishes the S114 tier-assignment sweep. S114 hand-classified four offenders (TTS synthesis,
/// script execution, marketplace install, one admin GET); this reflects over EVERY action in the Api
/// assembly and encodes the classification rule itself, so an endpoint added later that matches the
/// "expensive" name pattern (synthesis/execution/upload/install/publish/import/export/bulk/…) fails here
/// until it is explicitly tiered, and a plain GET stuck on an admin/write tier fails until it moves to
/// <see cref="RateLimitPolicyNames.Read"/>.
///
/// The effective tier is resolved the same way <see cref="RateLimitReadTierConvention"/> and ASP.NET's own
/// attribute-inheritance resolve it at runtime: an action's own <see cref="EnableRateLimitingAttribute"/>
/// wins; otherwise a controller's OWN (non-inherited) attribute wins and disables the GET/HEAD convention
/// (mirroring the real <see cref="IControllerModelConvention"/>); otherwise GET/HEAD gets
/// <see cref="RateLimitPolicyNames.Read"/> by convention; otherwise the action inherits
/// <see cref="RateLimitPolicyNames.WriteCheap"/> from <c>BaseController</c>.
/// </summary>
public sealed class RateLimitTierClassificationTests
{
    /// <summary>Permit limit per tier, ascending strictness. Used to prove an "expensive" action landed on
    /// a tier at least as strict as write-expensive (stricter tiers — security-sensitive — also pass).</summary>
    private static readonly Dictionary<string, int> PermitLimitByPolicy = new(
        StringComparer.Ordinal
    )
    {
        [SecuritySensitiveRateLimitPolicy.PolicyName] =
            SecuritySensitiveRateLimitPolicy.PermitLimit,
        [RateLimitPolicyNames.WriteExpensive] = WriteExpensiveRateLimitPolicy.PermitLimit,
        [RateLimitPolicyNames.Admin] = AdminRateLimitPolicy.PermitLimit,
        [RateLimitPolicyNames.Auth] = AuthRateLimitPolicy.PermitLimit,
        [RateLimitPolicyNames.WriteCheap] = WriteCheapRateLimitPolicy.PermitLimit,
        [RateLimitPolicyNames.DevicePoll] = DevicePollRateLimitPolicy.PermitLimit,
        [RateLimitPolicyNames.Anonymous] = AnonymousRateLimitPolicy.PermitLimit,
        [RateLimitPolicyNames.Read] = ReadRateLimitPolicy.PermitLimit,
    };

    /// <summary>
    /// Action-name pattern for work that is expensive enough to need its own throttle budget:
    /// synthesis, script/pipeline execution (dry-run counts — it still runs the real sandbox/engine),
    /// file upload, install (never "uninstall" — a plain delete), publish (never "publisher" — token CRUD),
    /// import/export, bulk projection rebuild/replay/migrate/reindex, and cross-tenant sync/fan-out.
    /// </summary>
    private static readonly Regex ExpensiveActionNamePattern = new(
        @"(Synthes|Upload|(?<!Un)Install|Publish(?!er)|Import|Export|Bulk|Replay|Rebuild|Reindex|Migrate|Compile|Sync|Fanout|Broadcast|TestVoice|TestRun|Execute)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>Tiers that are deliberate, standalone security/anonymity surfaces — an action landing here
    /// is never "wrong" regardless of its name, so the expensive-name check skips them.</summary>
    private static readonly HashSet<string> ExemptFromExpensiveNameCheck = new(
        StringComparer.Ordinal
    )
    {
        RateLimitPolicyNames.Auth,
        RateLimitPolicyNames.DevicePoll,
        RateLimitPolicyNames.Anonymous,
        RateLimitPolicyNames.Admin,
        SecuritySensitiveRateLimitPolicy.PolicyName,
    };

    private readonly record struct ActionRecord(
        Type Controller,
        MethodInfo Method,
        string EffectiveTier
    );

    private static IEnumerable<ActionRecord> AllActions()
    {
        List<Type> controllerTypes = typeof(BaseController)
            .Assembly.GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false }
                && typeof(BaseController).IsAssignableFrom(type)
            )
            .ToList();

        foreach (Type controllerType in controllerTypes)
        {
            bool controllerHasOwnPolicy =
                controllerType.IsDefined(typeof(EnableRateLimitingAttribute), inherit: false)
                || controllerType.IsDefined(typeof(DisableRateLimitingAttribute), inherit: false);

            string? controllerOwnTier = controllerHasOwnPolicy
                ? controllerType
                    .GetCustomAttribute<EnableRateLimitingAttribute>(inherit: false)
                    ?.PolicyName
                : null;

            foreach (
                MethodInfo method in controllerType.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            {
                bool isHttpAction = method
                    .GetCustomAttributes()
                    .OfType<HttpMethodAttribute>()
                    .Any();
                if (!isHttpAction)
                    continue;

                EnableRateLimitingAttribute? ownAction =
                    method.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: false);

                string effectiveTier;
                if (ownAction is not null)
                {
                    effectiveTier = ownAction.PolicyName ?? string.Empty;
                }
                else if (controllerHasOwnPolicy)
                {
                    // A controller-level [DisableRateLimiting] with no per-action override has no tier —
                    // ControllerSecurityBaselineTests already fails that case; skip it here.
                    if (string.IsNullOrEmpty(controllerOwnTier))
                        continue;
                    effectiveTier = controllerOwnTier;
                }
                else
                {
                    effectiveTier = IsGetOrHead(method)
                        ? RateLimitPolicyNames.Read
                        : RateLimitPolicyNames.WriteCheap;
                }

                yield return new ActionRecord(controllerType, method, effectiveTier);
            }
        }
    }

    [Fact]
    public void Every_expensive_named_action_is_on_write_expensive_or_stricter()
    {
        List<string> violations = AllActions()
            // GETs are judged by the separate read-tier rule below — a heavy read (GdprController.ExportData,
            // BundlesController.ListInstalled) stays on the generous per-user "read" tier by design; it is
            // not required to also earn the per-channel "write-expensive" partition.
            .Where(action => !IsGetOrHead(action.Method))
            .Where(action => !ExemptFromExpensiveNameCheck.Contains(action.EffectiveTier))
            .Where(action => ExpensiveActionNamePattern.IsMatch(action.Method.Name))
            .Where(action =>
                !PermitLimitByPolicy.TryGetValue(action.EffectiveTier, out int limit)
                || limit > WriteExpensiveRateLimitPolicy.PermitLimit
            )
            .Select(action =>
                $"{action.Controller.Name}.{action.Method.Name} is on '{action.EffectiveTier}' "
                + $"but its name indicates expensive work (synthesis/execution/upload/install/publish/import/export/bulk)"
            )
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Expensive-looking actions not tiered at write-expensive or stricter:\n"
                + string.Join('\n', violations)
        );
    }

    [Fact]
    public void Every_plain_get_is_off_the_admin_and_write_cheap_tiers()
    {
        List<string> violations = AllActions()
            .Where(action => IsGetOrHead(action.Method))
            .Where(action =>
                action.EffectiveTier == RateLimitPolicyNames.Admin
                || action.EffectiveTier == RateLimitPolicyNames.WriteCheap
            )
            .Select(action =>
                $"{action.Controller.Name}.{action.Method.Name} is a GET/HEAD stuck on '{action.EffectiveTier}' "
                + $"— it belongs on '{RateLimitPolicyNames.Read}'"
            )
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "GET/HEAD actions on the wrong tier:\n" + string.Join('\n', violations)
        );
    }

    private static bool IsGetOrHead(MethodInfo method) =>
        method
            .GetCustomAttributes()
            .OfType<HttpMethodAttribute>()
            .Any(attribute =>
                attribute.HttpMethods.Any(verb =>
                    string.Equals(verb, "GET", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(verb, "HEAD", StringComparison.OrdinalIgnoreCase)
                )
            );
}
