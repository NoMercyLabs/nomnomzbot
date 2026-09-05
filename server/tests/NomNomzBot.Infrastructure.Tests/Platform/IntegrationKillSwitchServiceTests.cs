// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Infrastructure.Platform;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// Proves the per-integration kill switch (S-ADMIN-5): a per-tenant override read by the code that gates the
/// integration produces DIFFERENT behaviour for two tenants, and any failure evaluating the underlying flag
/// degrades to a clean disabled result rather than an unhandled exception reaching the dependent feature.
/// </summary>
public sealed class IntegrationKillSwitchServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("0192a000-0000-7000-8000-0000000000aa");
    private static readonly Guid TenantB = Guid.Parse("0192a000-0000-7000-8000-0000000000bb");

    private static (IntegrationKillSwitchService Sut, IFeatureFlagService FeatureFlags) Build()
    {
        IFeatureFlagService featureFlags = Substitute.For<IFeatureFlagService>();
        ILogger<IntegrationKillSwitchService> logger = Substitute.For<
            ILogger<IntegrationKillSwitchService>
        >();
        return (new(featureFlags, logger), featureFlags);
    }

    [Fact]
    public async Task A_per_tenant_kill_switch_disables_only_the_killed_tenant()
    {
        (IntegrationKillSwitchService sut, IFeatureFlagService featureFlags) = Build();

        // Tenant A carries an unexpired override that kills the integration; tenant B has none, so it rides
        // the global (enabled) default — exactly the override-wins-over-global precedence the flag evaluator
        // implements, now proven to actually change the GATED behaviour, not merely the persisted row.
        featureFlags
            .EvaluateAsync("integration:spotify", TenantA, Arg.Any<CancellationToken>())
            .Returns(
                new FeatureFlagEvaluation(true, false, FeatureEntitlementReason.Unavailable, null)
            );
        featureFlags
            .EvaluateAsync("integration:spotify", TenantB, Arg.Any<CancellationToken>())
            .Returns(new FeatureFlagEvaluation(true, true, null, null));

        IntegrationAvailability killedTenant = await sut.CheckAsync("spotify", TenantA);
        IntegrationAvailability liveTenant = await sut.CheckAsync("spotify", TenantB);

        killedTenant.Enabled.Should().BeFalse();
        killedTenant.Reason.Should().NotBeNull();
        liveTenant.Enabled.Should().BeTrue();
        liveTenant.Reason.Should().BeNull();
    }

    [Fact]
    public async Task An_integration_with_no_flag_defined_is_available_by_default()
    {
        (IntegrationKillSwitchService sut, IFeatureFlagService featureFlags) = Build();
        featureFlags
            .EvaluateAsync("integration:discord", TenantA, Arg.Any<CancellationToken>())
            .Returns(new FeatureFlagEvaluation(false, false, null, null));

        (await sut.CheckAsync("discord", TenantA)).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_flag_evaluation_failure_degrades_to_disabled_instead_of_throwing()
    {
        (IntegrationKillSwitchService sut, IFeatureFlagService featureFlags) = Build();
        featureFlags
            .EvaluateAsync("integration:spotify", TenantA, Arg.Any<CancellationToken>())
            .Returns<Task<FeatureFlagEvaluation>>(_ =>
                throw new InvalidOperationException("database unreachable")
            );

        IntegrationAvailability result = await sut.CheckAsync("spotify", TenantA);

        result.Enabled.Should().BeFalse();
        result.Reason.Should().NotBeNull();
    }
}
