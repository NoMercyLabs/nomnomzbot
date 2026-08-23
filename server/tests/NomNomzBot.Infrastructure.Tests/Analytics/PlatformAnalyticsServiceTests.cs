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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NomNomzBot.Infrastructure.Services.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the platform stats service (analytics.md §3.4) self-gates on the DEPLOYMENT MODE (fix D2), never on
/// whether any <c>IamPrincipal</c> row exists: self-host returns FEATURE_DISABLED even when principal rows are
/// present (the bootstrap-owner-principal inversion the old row-count check produced), while a SaaS deployment
/// aggregates the no-PII channel daily (M.8) across every tenant.
/// </summary>
public sealed class PlatformAnalyticsServiceTests
{
    private static readonly DateOnly From = new(2026, 6, 20);
    private static readonly DateOnly To = new(2026, 6, 22);

    private static (PlatformAnalyticsService Sut, AuthDbContext Db) Build(
        DeploymentMode mode = DeploymentMode.SelfHostLite
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new(db, new(mode)), db);
    }

    private static ChannelAnalyticsDaily Daily(Guid channel, long messages) =>
        new()
        {
            BroadcasterId = channel,
            ActivityDate = new(2026, 6, 22),
            TotalMessages = messages,
        };

    [Fact]
    public async Task Self_host_returns_feature_disabled()
    {
        (PlatformAnalyticsService sut, AuthDbContext db) = Build();
        db.ChannelAnalyticsDailies.Add(Daily(Guid.NewGuid(), 10));
        await db.SaveChangesAsync();

        Result<PlatformAnalyticsDto> result = await sut.GetPlatformStatsAsync(From, To);

        result.ErrorCode.Should().Be("FEATURE_DISABLED");
    }

    [Fact]
    public async Task Self_host_returns_feature_disabled_even_when_iam_principal_rows_exist()
    {
        // Fix D2 inversion: self-host bootstraps a real owner IamPrincipal (S086), so principal existence can
        // no longer decide SaaS-ness. Before the fix this returned success and aggregated tenant data.
        (PlatformAnalyticsService sut, AuthDbContext db) = Build(DeploymentMode.SelfHostLite);
        db.IamPrincipals.Add(
            new()
            {
                Name = "owner",
                PrincipalType = IamPrincipalType.Employee,
                IsActive = true,
            }
        );
        db.ChannelAnalyticsDailies.Add(Daily(Guid.NewGuid(), 10));
        await db.SaveChangesAsync();

        Result<PlatformAnalyticsDto> result = await sut.GetPlatformStatsAsync(From, To);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("FEATURE_DISABLED");
    }

    [Fact]
    public async Task Saas_aggregates_channel_daily_across_tenants()
    {
        (PlatformAnalyticsService sut, AuthDbContext db) = Build(DeploymentMode.Saas);
        db.IamPrincipals.Add(
            new()
            {
                Name = "operator",
                PrincipalType = IamPrincipalType.Employee,
                IsActive = true,
            }
        );
        db.ChannelAnalyticsDailies.Add(Daily(Guid.NewGuid(), 10));
        db.ChannelAnalyticsDailies.Add(Daily(Guid.NewGuid(), 20));
        await db.SaveChangesAsync();

        Result<PlatformAnalyticsDto> result = await sut.GetPlatformStatsAsync(From, To);

        result.IsSuccess.Should().BeTrue();
        result.Value.ActiveChannels.Should().Be(2);
        result.Value.TotalMessages.Should().Be(30);
    }

    [Fact]
    public async Task Saas_returns_feature_disabled_when_no_iam_principal_rows_exist()
    {
        // Fix D2: SaaS-ness is the deployment mode, not row presence — a fresh SaaS tenant with zero
        // IamPrincipal rows still gets the cross-tenant view.
        (PlatformAnalyticsService sut, AuthDbContext db) = Build(DeploymentMode.Saas);
        db.ChannelAnalyticsDailies.Add(Daily(Guid.NewGuid(), 5));
        await db.SaveChangesAsync();

        Result<PlatformAnalyticsDto> result = await sut.GetPlatformStatsAsync(From, To);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMessages.Should().Be(5);
    }
}
