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
using NomNomzBot.Application.DTOs.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the scope-diagnostics matrix is truthful against the channel's stored Twitch grant: a granted scope
/// reads back <c>Granted=true</c>, an absent one <c>Granted=false</c>, every row is a progressive scope gated
/// by its own feature, the matrix covers every <see cref="FeatureScopeMap"/> entry, and an unconnected tenant
/// is a <c>NOT_FOUND</c> (so the dashboard can render "not connected" rather than 500).
/// </summary>
public sealed class TwitchScopeDiagnosticsServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");

    private static async Task SeedTwitchConnectionAsync(AuthDbContext db, params string[] scopes)
    {
        IntegrationConnection connection = new()
        {
            BroadcasterId = Tenant,
            Provider = AuthEnums.IntegrationProvider.Twitch,
            Status = AuthEnums.IntegrationStatus.Connected,
            Scopes = [.. scopes],
        };
        db.IntegrationConnections.Add(connection);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetScopeDiagnostics_ReportsConnectionStatus_GrantedScopes_AndAFullMatrix()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedTwitchConnectionAsync(db, "channel:read:subscriptions", "bits:read");
        TwitchScopeDiagnosticsService sut = new(db);

        Result<TwitchScopeDiagnosticsDto> result = await sut.GetScopeDiagnosticsAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        TwitchScopeDiagnosticsDto dto = result.Value;
        dto.ConnectionStatus.Should().Be(AuthEnums.IntegrationStatus.Connected);
        dto.GrantedScopes.Should().BeEquivalentTo(["channel:read:subscriptions", "bits:read"]);

        // Every feature→scope entry in the registry produces exactly one matrix row.
        int expectedRows = FeatureScopeMap.Features.Sum(f => f.Value.Count);
        dto.Requirements.Should().HaveCount(expectedRows);

        // Each row is a progressive scope gated by its own feature — the matrix invariant.
        dto.Requirements.Should()
            .OnlyContain(r => r.IsProgressive && r.GatedByFeature == r.Feature);
    }

    [Fact]
    public async Task GetScopeDiagnostics_MarksGrantedAndMissingScopesCorrectly()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedTwitchConnectionAsync(db, "channel:read:subscriptions");
        TwitchScopeDiagnosticsService sut = new(db);

        Result<TwitchScopeDiagnosticsDto> result = await sut.GetScopeDiagnosticsAsync(Tenant);

        // The seeded scope is granted, labelled with the feature it unlocks.
        TwitchScopeRequirementDto subs = result.Value.Requirements.Single(r =>
            r.Scope == "channel:read:subscriptions"
        );
        subs.Granted.Should().BeTrue();
        subs.Feature.Should().Be("subscriptions");

        // A scope the channel did not grant reads back as missing (feature-gated, not an error).
        TwitchScopeRequirementDto raids = result.Value.Requirements.First(r =>
            r.Scope == "channel:manage:raids"
        );
        raids.Granted.Should().BeFalse();
        raids.Feature.Should().Be("raids");
    }

    [Fact]
    public async Task GetScopeDiagnostics_WhenNoTwitchConnection_ReturnsNotFound()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        TwitchScopeDiagnosticsService sut = new(db);

        Result<TwitchScopeDiagnosticsDto> result = await sut.GetScopeDiagnosticsAsync(Tenant);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
