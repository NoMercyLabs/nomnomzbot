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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Services.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the platform stats service (analytics.md §3.4): self-host (no platform IAM) returns FEATURE_DISABLED,
/// while a SaaS deployment aggregates the no-PII channel daily (M.8) across every tenant.
/// </summary>
public sealed class PlatformAnalyticsServiceTests
{
    private static readonly DateOnly From = new(2026, 6, 20);
    private static readonly DateOnly To = new(2026, 6, 22);

    private static (PlatformAnalyticsService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new PlatformAnalyticsService(db), db);
    }

    private static ChannelAnalyticsDaily Daily(Guid channel, long messages) =>
        new()
        {
            BroadcasterId = channel,
            ActivityDate = new DateOnly(2026, 6, 22),
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
    public async Task Saas_aggregates_channel_daily_across_tenants()
    {
        (PlatformAnalyticsService sut, AuthDbContext db) = Build();
        db.IamPrincipals.Add(
            new IamPrincipal
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
}
