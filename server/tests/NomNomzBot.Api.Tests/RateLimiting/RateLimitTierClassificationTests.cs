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
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Api.Tests.Hosting;

namespace NomNomzBot.Api.Tests.RateLimiting;

/// <summary>
/// S118 — finishes the S114 tier-assignment sweep. S114 hand-classified four offenders (TTS synthesis,
/// script execution, marketplace install, one admin GET); this reads EVERY routed action of the API and
/// encodes the classification rule itself, so an endpoint added later that matches the "expensive" name
/// pattern (synthesis/execution/upload/install/publish/import/export/bulk/…) fails here until it is
/// explicitly tiered, and a plain GET stuck on an admin/write tier fails until it moves to
/// <see cref="RateLimitPolicyNames.Read"/>.
///
/// The effective tier is the one the rate limiter middleware reads at runtime: the
/// <see cref="EnableRateLimitingAttribute"/> on the endpoint metadata of the endpoint table built by the
/// production controller setup, which includes what <see cref="RateLimitReadTierConvention"/> applied.
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
        [RateLimitPolicyNames.Session] = SessionRateLimitPolicy.PermitLimit,
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
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
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
        string ActionName,
        string EffectiveTier,
        bool IsGetOrHead
    );

    private static IEnumerable<ActionRecord> AllActions()
    {
        foreach (RouteEndpoint endpoint in ApiEndpointSurface.All)
        {
            ControllerActionDescriptor descriptor =
                endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!;

            // An endpoint with [DisableRateLimiting] has no tier; ControllerSecurityBaselineTests fails that case.
            string? tier = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
            if (
                string.IsNullOrEmpty(tier)
                || endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null
            )
                continue;

            IReadOnlyList<string> verbs = endpoint
                .Metadata.GetMetadata<HttpMethodMetadata>()!
                .HttpMethods;
            yield return new(
                descriptor.ControllerTypeInfo,
                descriptor.MethodInfo.Name,
                tier,
                verbs.Contains("GET", StringComparer.OrdinalIgnoreCase)
                    || verbs.Contains("HEAD", StringComparer.OrdinalIgnoreCase)
            );
        }
    }

    [Fact]
    public void Every_expensive_named_action_is_on_write_expensive_or_stricter()
    {
        List<string> violations =
        [
            .. AllActions()
                // GETs are judged by the separate read-tier rule below — a heavy read (GdprController.ExportData,
                // BundlesController.ListInstalled) stays on the generous per-user "read" tier by design; it is
                // not required to also earn the per-channel "write-expensive" partition.
                .Where(action => !action.IsGetOrHead)
                .Where(action => !ExemptFromExpensiveNameCheck.Contains(action.EffectiveTier))
                .Where(action => ExpensiveActionNamePattern.IsMatch(action.ActionName))
                .Where(action =>
                    !PermitLimitByPolicy.TryGetValue(action.EffectiveTier, out int limit)
                    || limit > WriteExpensiveRateLimitPolicy.PermitLimit
                )
                .Select(action =>
                    $"{action.Controller.Name}.{action.ActionName} is on '{action.EffectiveTier}' "
                    + $"but its name indicates expensive work (synthesis/execution/upload/install/publish/import/export/bulk)"
                )
                .Distinct()
                .OrderBy(message => message, StringComparer.Ordinal),
        ];

        Assert.True(
            violations.Count == 0,
            "Expensive-looking actions not tiered at write-expensive or stricter:\n"
                + string.Join('\n', violations)
        );
    }

    [Fact]
    public void Every_plain_get_is_off_the_admin_and_write_cheap_tiers()
    {
        List<string> violations =
        [
            .. AllActions()
                .Where(action => action.IsGetOrHead)
                .Where(action =>
                    action.EffectiveTier == RateLimitPolicyNames.Admin
                    || action.EffectiveTier == RateLimitPolicyNames.WriteCheap
                )
                .Select(action =>
                    $"{action.Controller.Name}.{action.ActionName} is a GET/HEAD stuck on '{action.EffectiveTier}' "
                    + $"— it belongs on '{RateLimitPolicyNames.Read}'"
                )
                .Distinct()
                .OrderBy(message => message, StringComparer.Ordinal),
        ];

        Assert.True(
            violations.Count == 0,
            "GET/HEAD actions on the wrong tier:\n" + string.Join('\n', violations)
        );
    }

    [Theory]
    [InlineData(nameof(AuthController.GetCurrentUser), "read")]
    [InlineData(nameof(AuthController.GetMyIdentities), "read")]
    [InlineData(nameof(AuthController.RefreshToken), "session")]
    public void Signed_in_session_calls_are_off_the_login_brute_force_tier(
        string actionName,
        string expectedTier
    )
    {
        ActionRecord action = AllActions()
            .Single(record =>
                record.Controller == typeof(AuthController) && record.ActionName == actionName
            );

        Assert.NotEqual(RateLimitPolicyNames.Auth, action.EffectiveTier);
        Assert.Equal(expectedTier, action.EffectiveTier);
    }

    [Theory]
    [InlineData(nameof(AuthController.StartTwitchOAuth))]
    [InlineData(nameof(AuthController.ExchangeCode))]
    [InlineData(nameof(AuthController.StartBotOAuth))]
    public void Credential_exchange_endpoints_stay_on_the_login_brute_force_tier(string actionName)
    {
        ActionRecord action = AllActions()
            .Single(record =>
                record.Controller == typeof(AuthController) && record.ActionName == actionName
            );

        Assert.Equal(RateLimitPolicyNames.Auth, action.EffectiveTier);
    }
}
