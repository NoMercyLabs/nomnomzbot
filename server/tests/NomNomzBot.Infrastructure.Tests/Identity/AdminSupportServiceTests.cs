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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the cross-tenant support desk (S-ADMIN-7a): the operator finds a person who lives in a tenant that
/// is NOT their own and reads that person's REAL state — the trust/heat numbers actually persisted, the
/// community standing actually recorded, the entitlement the billing service actually resolves — each fact
/// carrying the tenant it belongs to. A datum the system does not hold is ABSENT, never a zero row. Every
/// lookup gates and audits FIRST, naming the acting operator and the subject.
/// </summary>
public sealed class AdminSupportServiceTests
{
    private static readonly PaginationParams Page = new(1, 25, null, null);
    private const string Why = "Ticket #8812 — viewer reports being muted everywhere.";

    private static (AdminSupportService Sut, AuthDbContext Db, IBillingTierService Billing) Build(
        DeploymentMode mode = DeploymentMode.Saas
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IBillingTierService billing = Substitute.For<IBillingTierService>();
        billing
            .GetEntitlementAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<EntitlementDto>("No entitlement for this tenant.", "NOT_FOUND")
            );
        AdminSupportService sut = new(
            db,
            new PlatformIamService(db, new RecordingEventBus(), TimeProvider.System, new(mode)),
            billing
        );
        return (sut, db, billing);
    }

    /// <summary>An operator IAM principal holding exactly <paramref name="permissionKeys"/> (SaaS on).</summary>
    private static Guid SeedOperator(AuthDbContext db, params string[] permissionKeys)
    {
        Guid principalId = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new()
            {
                Id = principalId,
                PrincipalType = IamPrincipalType.Employee,
                Name = "support-operator",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new() { Id = roleId, Name = $"role-{roleId}" });
        foreach (string key in permissionKeys)
        {
            Guid permissionId = Guid.NewGuid();
            db.IamPermissions.Add(
                new()
                {
                    Id = permissionId,
                    Key = key,
                    Category = IamCategory.Iam,
                }
            );
            db.IamRolePermissions.Add(new() { RoleId = roleId, PermissionId = permissionId });
        }
        db.IamRoleAssignments.Add(
            new()
            {
                PrincipalId = principalId,
                RoleId = roleId,
                AssignedByPrincipalId = principalId,
            }
        );
        return principalId;
    }

    private static Guid SeedViewer(AuthDbContext db, string name, string twitchId)
    {
        Guid userId = Guid.NewGuid();
        db.Users.Add(
            new()
            {
                Id = userId,
                Username = name,
                UsernameNormalized = name.ToLowerInvariant(),
                DisplayName = name,
                TwitchUserId = twitchId,
            }
        );
        return userId;
    }

    private static Guid SeedTenant(AuthDbContext db, string name, Guid ownerUserId)
    {
        Guid channelId = Guid.NewGuid();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = ownerUserId,
                TwitchChannelId = $"tw-{name}",
                Name = name,
                NameNormalized = name.ToLowerInvariant(),
            }
        );
        return channelId;
    }

    [Fact]
    public async Task Search_finds_a_person_who_lives_only_in_someone_elses_tenant()
    {
        (AdminSupportService sut, AuthDbContext db, _) = Build();
        Guid operatorPrincipal = SeedOperator(db, IamPermissionKeys.UserSupportView);

        // The subject owns NOTHING. They exist only as a viewer inside a channel owned by a different
        // person — the exact case a tenant-scoped query would hide.
        Guid otherOwner = SeedViewer(db, "other_streamer", "tw-owner");
        Guid foreignTenant = SeedTenant(db, "other_streamer", otherOwner);
        Guid subject = SeedViewer(db, "wandering_viewer", "tw-4242");
        db.ChannelCommunityStandings.Add(
            new()
            {
                BroadcasterId = foreignTenant,
                UserId = subject,
                Standing = CommunityStanding.Subscriber,
                LevelValue = 20,
                Source = StandingSource.EventSubBadge,
            }
        );
        await db.SaveChangesAsync();

        Result<PagedList<SupportPersonSearchResultDto>> result = await sut.SearchPeopleAsync(
            operatorPrincipal,
            "wandering",
            Why,
            Page
        );

        result.IsSuccess.Should().BeTrue();
        SupportPersonSearchResultDto found = result
            .Value.Items.Should()
            .ContainSingle(r => r.UserId == subject)
            .Subject;
        found.Username.Should().Be("wandering_viewer");
        found.TwitchUserId.Should().Be("tw-4242");
        found
            .TenantCount.Should()
            .Be(1, "the subject is known in exactly one channel — not their own");
    }

    [Fact]
    public async Task Search_without_the_support_key_is_refused_and_returns_no_people()
    {
        (AdminSupportService sut, AuthDbContext db, _) = Build();
        Guid operatorPrincipal = SeedOperator(db, IamPermissionKeys.TenantRead);
        SeedViewer(db, "wandering_viewer", "tw-4242");
        await db.SaveChangesAsync();

        Result<PagedList<SupportPersonSearchResultDto>> result = await sut.SearchPeopleAsync(
            operatorPrincipal,
            "wandering",
            Why,
            Page
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Person_lookup_writes_an_audit_row_naming_the_operator_and_the_subject()
    {
        (AdminSupportService sut, AuthDbContext db, _) = Build();
        Guid operatorPrincipal = SeedOperator(db, IamPermissionKeys.UserSupportView);
        Guid subject = SeedViewer(db, "wandering_viewer", "tw-4242");
        await db.SaveChangesAsync();

        Result<SupportPersonViewDto> result = await sut.GetPersonAsync(
            operatorPrincipal,
            subject,
            Why
        );

        result.IsSuccess.Should().BeTrue();
        IamAuditLog audit = await db.IamAuditLogs.SingleAsync(a =>
            a.Permission == IamPermissionKeys.UserSupportView
        );
        audit.PrincipalId.Should().Be(operatorPrincipal, "the ACTING operator is named");
        audit.TargetResource.Should().Be($"user:{subject}", "the SUBJECT is named");
        audit.Justification.Should().Be(Why);
        audit.Outcome.Should().Be(IamOutcome.Allowed);
    }

    [Fact]
    public async Task Person_lookup_denial_is_audited_too_and_reads_no_subject_data()
    {
        (AdminSupportService sut, AuthDbContext db, _) = Build();
        Guid operatorPrincipal = SeedOperator(db, IamPermissionKeys.TenantRead);
        Guid subject = SeedViewer(db, "wandering_viewer", "tw-4242");
        await db.SaveChangesAsync();

        Result<SupportPersonViewDto> result = await sut.GetPersonAsync(
            operatorPrincipal,
            subject,
            Why
        );

        result.IsFailure.Should().BeTrue();
        IamAuditLog audit = await db.IamAuditLogs.SingleAsync(a =>
            a.Permission == IamPermissionKeys.UserSupportView
        );
        audit.Outcome.Should().Be(IamOutcome.Denied);
        audit.TargetResource.Should().Be($"user:{subject}");
    }

    [Fact]
    public async Task Person_lookup_requires_a_justification()
    {
        (AdminSupportService sut, AuthDbContext db, _) = Build();
        Guid operatorPrincipal = SeedOperator(db, IamPermissionKeys.UserSupportView);
        Guid subject = SeedViewer(db, "wandering_viewer", "tw-4242");
        await db.SaveChangesAsync();

        Result<SupportPersonViewDto> result = await sut.GetPersonAsync(
            operatorPrincipal,
            subject,
            "  "
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.IamAuditLogs.CountAsync())
            .Should()
            .Be(0, "the gate never ran, so nothing is audited");
    }

    [Fact]
    public async Task Person_view_carries_the_exact_persisted_values_each_labeled_with_its_tenant()
    {
        (AdminSupportService sut, AuthDbContext db, IBillingTierService billing) = Build();
        Guid operatorPrincipal = SeedOperator(db, IamPermissionKeys.UserSupportView);

        Guid subject = SeedViewer(db, "wandering_viewer", "tw-4242");
        Guid ownTenant = SeedTenant(db, "wandering_viewer", subject);
        Guid otherOwner = SeedViewer(db, "other_streamer", "tw-owner");
        Guid foreignTenant = SeedTenant(db, "other_streamer", otherOwner);

        db.UserIdentities.Add(
            new()
            {
                UserId = subject,
                Provider = AuthEnums.Platform.Twitch,
                ProviderUserId = "tw-4242",
                ProviderUsername = "wandering_viewer",
                IsPrimary = true,
                LinkedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        db.ChannelCommunityStandings.Add(
            new()
            {
                BroadcasterId = foreignTenant,
                UserId = subject,
                Standing = CommunityStanding.Vip,
                LevelValue = 30,
                Source = StandingSource.EventSubBadge,
                SubTier = "2000",
            }
        );
        db.UserTrustScores.Add(
            new()
            {
                BroadcasterId = foreignTenant,
                SubjectUserId = subject,
                SubjectTwitchUserId = "tw-4242",
                TrustScore = 73.5m,
                HeatScore = 12.25m,
                ComputedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        db.ChannelModerationStandings.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = foreignTenant,
                Provider = AuthEnums.Platform.Twitch,
                UserId = "tw-4242",
                Standing = ModerationStanding.Muted,
                Reason = "Repeated link spam",
            }
        );
        db.EntitlementGrants.Add(
            new()
            {
                BroadcasterId = ownTenant,
                GrantedTierId = Guid.Parse("0199a000-0000-7000-8000-0000000000e1"),
                Reason = "Support case #8812 — 30-day comp",
                IssuedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();

        billing
            .GetEntitlementAsync(ownTenant, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new EntitlementDto("pro", true, false, new Dictionary<string, long>())
                )
            );

        Result<SupportPersonViewDto> result = await sut.GetPersonAsync(
            operatorPrincipal,
            subject,
            Why
        );

        result.IsSuccess.Should().BeTrue();
        SupportPersonViewDto view = result.Value;

        SupportPersonTrustScoreDto trust = view.TrustScores.Should().ContainSingle().Subject;
        trust.TrustScore.Should().Be(73.5m);
        trust.HeatScore.Should().Be(12.25m);
        trust
            .BroadcasterId.Should()
            .Be(foreignTenant, "the score belongs to the OTHER streamer's channel");
        trust.ChannelName.Should().Be("other_streamer");

        SupportPersonCommunityStandingDto standing = view
            .CommunityStandings.Should()
            .ContainSingle()
            .Subject;
        standing.Standing.Should().Be(nameof(CommunityStanding.Vip));
        standing.LevelValue.Should().Be(30);
        standing.SubTier.Should().Be("2000");
        standing.BroadcasterId.Should().Be(foreignTenant);

        SupportPersonModerationStandingDto muted = view
            .ModerationStandings.Should()
            .ContainSingle()
            .Subject;
        muted.Standing.Should().Be(ModerationStanding.Muted);
        muted.Reason.Should().Be("Repeated link spam");
        muted.BroadcasterId.Should().Be(foreignTenant);

        SupportPersonEntitlementDto entitlement = view
            .Entitlements.Should()
            .ContainSingle()
            .Subject;
        entitlement.TierKey.Should().Be("pro");
        entitlement
            .BroadcasterId.Should()
            .Be(ownTenant, "the comp is on the channel the subject OWNS");
        entitlement
            .Grants.Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Be("Support case #8812 — 30-day comp");

        view.Identities.Should().ContainSingle().Which.ProviderUserId.Should().Be("tw-4242");
    }

    [Fact]
    public async Task A_fact_the_system_does_not_hold_is_absent_never_a_zero_row()
    {
        (AdminSupportService sut, AuthDbContext db, _) = Build();
        Guid operatorPrincipal = SeedOperator(db, IamPermissionKeys.UserSupportView);
        // A brand-new chatter: a User row and nothing else. No trust score has ever been computed, no comp
        // was ever issued, no moderation action ever taken.
        Guid subject = SeedViewer(db, "brand_new_chatter", "tw-9001");
        await db.SaveChangesAsync();

        Result<SupportPersonViewDto> result = await sut.GetPersonAsync(
            operatorPrincipal,
            subject,
            Why
        );

        result.IsSuccess.Should().BeTrue();
        SupportPersonViewDto view = result.Value;
        view.TrustScores.Should()
            .BeEmpty("no score was ever computed — a 0.0 row would read as real data");
        view.Entitlements.Should().BeEmpty();
        view.ModerationStandings.Should().BeEmpty();
        view.ModerationHistory.Should().BeEmpty();
        view.CommunityStandings.Should().BeEmpty();
        view.IamRoles.Should().BeEmpty();
        view.PlatformConnections.Should().BeEmpty();
        view.Username.Should()
            .Be("brand_new_chatter", "the identity itself is still real and present");
    }
}
