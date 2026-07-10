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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.DTOs.Twitch;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Dashboard;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NomNomzBot.Infrastructure.Tests.Dashboard;

/// <summary>
/// Proves the render manifest (1) composes its four sources faithfully, (2) reveals a section ONLY
/// when the caller clears that surface's Gate-2 read floor — so a participant never sees what an
/// individual endpoint would withhold — and (3) enforces the load-bearing vs. best-effort contract:
/// access and features fail the whole manifest, integrations and scopes degrade to an empty section.
/// </summary>
public class RenderManifestServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BroadcasterId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>A caller who clears every aggregated surface's read floor (a broadcaster).</summary>
    private static ResolvedAccessDto FullAccess() =>
        new(
            UserId,
            BroadcasterId,
            EffectiveLevel: 100,
            CommunityStanding.Moderator,
            CommunityLevel: 10,
            ManagementRole.Broadcaster,
            ManagementLevel: 100,
            PermitRole: null,
            PermitCapabilities: [],
            WinningSource: "management",
            HeldActionKeys:
            [
                "feature:read",
                "integration:read",
                "twitch:diagnostics:read",
                "roles:read",
            ]
        );

    /// <summary>A pure participant — clears no management floor.</summary>
    private static ResolvedAccessDto ParticipantAccess() =>
        new(
            UserId,
            BroadcasterId,
            EffectiveLevel: 0,
            CommunityStanding.Everyone,
            CommunityLevel: 0,
            ManagementRole: null,
            ManagementLevel: 0,
            PermitRole: null,
            PermitCapabilities: [],
            WinningSource: "community",
            HeldActionKeys: []
        );

    private static List<FeatureStatusDto> SampleFeatures() =>
        [
            new(
                "song_requests",
                "Song Requests",
                "Viewers request songs",
                IsEnabled: true,
                DateTime.UnixEpoch,
                new[] { "channel:read:redemptions" }
            ),
        ];

    private static List<ChannelIntegrationDto> SampleIntegrations() =>
        [new("spotify", "Spotify", "Music", "Now playing overlays", Connected: true, "dj_cat")];

    private static MissingScopesDto SampleScopes() =>
        new(
            "connected",
            [new MissingScopeDto("channel:read:redemptions", ["song_requests"], true, false)]
        );

    private sealed record Harness(
        RenderManifestService Service,
        IRoleResolver Roles,
        IFeatureService Features,
        IIntegrationStatusService Integrations,
        IScopeNotificationService Scopes
    );

    /// <summary>Builds the service over substitutes; every section wired to its healthy sample.</summary>
    private static Harness CreateHealthy(ResolvedAccessDto access)
    {
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        IFeatureService features = Substitute.For<IFeatureService>();
        IIntegrationStatusService integrations = Substitute.For<IIntegrationStatusService>();
        IScopeNotificationService scopes = Substitute.For<IScopeNotificationService>();

        roles
            .ResolveAccessAsync(UserId, BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(access));
        features
            .GetFeaturesAsync(BroadcasterId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(SampleFeatures()));
        integrations
            .GetStatusesAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(SampleIntegrations()));
        scopes
            .GetMissingScopesAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(SampleScopes()));

        RenderManifestService service = new(
            roles,
            features,
            integrations,
            scopes,
            NullLogger<RenderManifestService>.Instance
        );
        return new Harness(service, roles, features, integrations, scopes);
    }

    [Fact]
    public async Task GetManifestAsync_CallerClearsEveryFloor_ComposesEverySectionFaithfully()
    {
        Harness harness = CreateHealthy(FullAccess());

        Result<RenderManifestDto> result = await harness.Service.GetManifestAsync(
            UserId,
            BroadcasterId
        );

        result.IsSuccess.Should().BeTrue();
        RenderManifestDto manifest = result.Value;

        // Each section carries its source DTO through intact (deep value equality).
        manifest.Access.Should().BeEquivalentTo(FullAccess());
        manifest.Features.Should().BeEquivalentTo(SampleFeatures());
        manifest.Integrations.Should().BeEquivalentTo(SampleIntegrations());
        manifest.Scopes.Should().BeEquivalentTo(SampleScopes());

        // Spot-check the load-bearing key fields the shell gates on.
        manifest.Access.ManagementRole.Should().Be(ManagementRole.Broadcaster);
        manifest.Access.HeldActionKeys.Should().Contain("feature:read");
        manifest
            .Features.Should()
            .ContainSingle(f => f.FeatureKey == "song_requests" && f.IsEnabled);
        manifest
            .Integrations.Should()
            .ContainSingle(i => i.Id == "spotify" && i.Connected && i.ConnectedAs == "dj_cat");
        manifest.Scopes.ConnectionStatus.Should().Be("connected");
        manifest.Scopes.Scopes.Should().ContainSingle(s => s.Scope == "channel:read:redemptions");
    }

    [Fact]
    public async Task GetManifestAsync_ParticipantWithoutFloors_RevealsAccessOnly_AndNeverQueriesGatedSources()
    {
        Harness harness = CreateHealthy(ParticipantAccess());

        Result<RenderManifestDto> result = await harness.Service.GetManifestAsync(
            UserId,
            BroadcasterId
        );

        result.IsSuccess.Should().BeTrue();
        RenderManifestDto manifest = result.Value;

        // The participant sees their own (management-less) access…
        manifest.Access.ManagementRole.Should().BeNull();
        manifest.Access.HeldActionKeys.Should().BeEmpty();
        // …and every gated section is empty.
        manifest.Features.Should().BeEmpty();
        manifest.Integrations.Should().BeEmpty();
        manifest.Scopes.Scopes.Should().BeEmpty();

        // Critically, the gated sources are NEVER queried — no data an individual endpoint would have
        // withheld can leak through the aggregate.
        await harness
            .Features.DidNotReceive()
            .GetFeaturesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await harness
            .Integrations.DidNotReceive()
            .GetStatusesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await harness
            .Scopes.DidNotReceive()
            .GetMissingScopesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetManifestAsync_IntegrationSourceReturnsFailure_DegradesToEmptySection()
    {
        Harness harness = CreateHealthy(FullAccess());
        harness
            .Integrations.GetStatusesAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<List<ChannelIntegrationDto>>("discord unreachable", "UPSTREAM"));

        Result<RenderManifestDto> result = await harness.Service.GetManifestAsync(
            UserId,
            BroadcasterId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Integrations.Should().BeEmpty();
        // Other sections remain fully populated.
        result.Value.Features.Should().ContainSingle(f => f.FeatureKey == "song_requests");
        result.Value.Scopes.Scopes.Should().ContainSingle();
    }

    [Fact]
    public async Task GetManifestAsync_ScopeSourceThrows_DegradesToEmptyScopeSection()
    {
        Harness harness = CreateHealthy(FullAccess());
        harness
            .Scopes.GetMissingScopesAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("token store offline"));

        Result<RenderManifestDto> result = await harness.Service.GetManifestAsync(
            UserId,
            BroadcasterId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Scopes.Scopes.Should().BeEmpty();
        // Integrations (a separate best-effort source) is unaffected.
        result.Value.Integrations.Should().ContainSingle(i => i.Id == "spotify");
    }

    [Fact]
    public async Task GetManifestAsync_ScopeSourceReturnsNotFound_DegradesToEmptyScopeSection()
    {
        Harness harness = CreateHealthy(FullAccess());
        harness
            .Scopes.GetMissingScopesAsync(BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<MissingScopesDto>("no twitch connection", "NOT_FOUND"));

        Result<RenderManifestDto> result = await harness.Service.GetManifestAsync(
            UserId,
            BroadcasterId
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Scopes.Scopes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetManifestAsync_AccessResolutionFails_FailsWholeManifestAndSkipsRest()
    {
        Harness harness = CreateHealthy(FullAccess());
        harness
            .Roles.ResolveAccessAsync(UserId, BroadcasterId, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedAccessDto>.Failure("channel not found", "NOT_FOUND"));

        Result<RenderManifestDto> result = await harness.Service.GetManifestAsync(
            UserId,
            BroadcasterId
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be("channel not found");
        result.ErrorCode.Should().Be("NOT_FOUND");
        // Access is evaluated first — no other source is queried when it fails.
        await harness
            .Features.DidNotReceive()
            .GetFeaturesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetManifestAsync_FeatureSourceFails_FailsWholeManifest()
    {
        Harness harness = CreateHealthy(FullAccess());
        harness
            .Features.GetFeaturesAsync(BroadcasterId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<List<FeatureStatusDto>>("feature store down", "UPSTREAM"));

        Result<RenderManifestDto> result = await harness.Service.GetManifestAsync(
            UserId,
            BroadcasterId
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be("feature store down");
    }
}
