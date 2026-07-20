// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Auth;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the Plane-C tenant operations (stream-admin.md §3.2): suspend/reinstate really flip the tenant's
/// lifecycle columns and emit <c>TenantSuspensionChangedEvent</c>; every op funnels through
/// <c>AuthorizePlatformAsync</c> (audited on SaaS, FORBIDDEN + denied-audit without the key); support access
/// is a time-boxed, tenant-narrowed <c>platform-support</c> assignment the caller can end; and the audit
/// search filters the append-only log.
/// </summary>
public sealed class PlatformAdminServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 3, 0, 0, TimeSpan.Zero);
    private static readonly PaginationParams Page = new(1, 25, null, null);

    private static (PlatformAdminService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        FakeTimeProvider clock = new(Now);
        PlatformAdminService sut = new(
            db,
            new PlatformIamService(db, bus, clock),
            Jwt(),
            bus,
            clock
        );
        return (sut, db, bus);
    }

    /// <summary>
    /// A real HS256 token service on the system clock (so a minted token is valid "now" for
    /// <c>ValidateAccessToken</c>). A second instance from this same fixed config verifies tokens the service
    /// under test minted — identical secret/issuer/audience is all validation needs.
    /// </summary>
    private static JwtTokenService Jwt() =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        { "Jwt:Secret", "super-secret-key-that-is-at-least-32-bytes-long!" },
                        { "Jwt:Issuer", "TestIssuer" },
                        { "Jwt:Audience", "TestAudience" },
                        { "Jwt:ExpiryMinutes", "60" },
                    }
                )
                .Build(),
            TimeProvider.System
        );

    /// <summary>An operator IAM principal bound to <paramref name="userId"/>, holding <paramref name="keys"/> via one role (SaaS on).</summary>
    private static Guid SeedPrincipalFor(
        AuthDbContext db,
        Guid userId,
        string name,
        params string[] keys
    )
    {
        Guid principalId = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new IamPrincipal
            {
                Id = principalId,
                PrincipalType = IamPrincipalType.Employee,
                UserId = userId,
                Name = name,
                IsActive = true,
            }
        );
        db.IamRoles.Add(new IamRole { Id = roleId, Name = $"role-{roleId}" });
        foreach (string key in keys)
        {
            Guid permissionId = Guid.NewGuid();
            db.IamPermissions.Add(
                new IamPermission
                {
                    Id = permissionId,
                    Key = key,
                    Category = IamCategory.Iam,
                }
            );
            db.IamRolePermissions.Add(
                new IamRolePermission { RoleId = roleId, PermissionId = permissionId }
            );
        }
        db.IamRoleAssignments.Add(
            new IamRoleAssignment
            {
                PrincipalId = principalId,
                RoleId = roleId,
                AssignedByPrincipalId = principalId,
            }
        );
        return principalId;
    }

    private static Guid SeedUser(AuthDbContext db, string name, bool isPlatformPrincipal)
    {
        Guid userId = Guid.NewGuid();
        db.Users.Add(
            new User
            {
                Id = userId,
                Username = name,
                UsernameNormalized = name,
                DisplayName = name,
                IsPlatformPrincipal = isPlatformPrincipal,
            }
        );
        return userId;
    }

    /// <summary>An IAM principal holding <paramref name="permissionKeys"/> via one role — SaaS mode on.</summary>
    private static Guid SeedPrincipal(AuthDbContext db, params string[] permissionKeys)
    {
        Guid principalId = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new IamPrincipal
            {
                Id = principalId,
                PrincipalType = IamPrincipalType.Employee,
                Name = "operator",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new IamRole { Id = roleId, Name = $"role-{roleId}" });
        foreach (string key in permissionKeys)
        {
            Guid permissionId = Guid.NewGuid();
            db.IamPermissions.Add(
                new IamPermission
                {
                    Id = permissionId,
                    Key = key,
                    Category = IamCategory.Iam,
                }
            );
            db.IamRolePermissions.Add(
                new IamRolePermission { RoleId = roleId, PermissionId = permissionId }
            );
        }
        db.IamRoleAssignments.Add(
            new IamRoleAssignment
            {
                PrincipalId = principalId,
                RoleId = roleId,
                AssignedByPrincipalId = principalId,
            }
        );
        return principalId;
    }

    private static Guid SeedTenant(AuthDbContext db, string name = "stoney_eagle")
    {
        Guid ownerId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        db.Users.Add(
            new User
            {
                Id = ownerId,
                Username = name,
                UsernameNormalized = name,
                DisplayName = name,
            }
        );
        db.Channels.Add(
            new Channel
            {
                Id = channelId,
                OwnerUserId = ownerId,
                TwitchChannelId = "tw-1",
                Name = name,
                NameNormalized = name,
            }
        );
        return channelId;
    }

    [Fact]
    public async Task Suspend_flips_the_lifecycle_columns_audits_and_publishes()
    {
        (PlatformAdminService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid principal = SeedPrincipal(db, "tenant:suspend");
        Guid tenant = SeedTenant(db);
        await db.SaveChangesAsync();

        Result result = await sut.SuspendTenantAsync(
            principal,
            tenant,
            new SuspendTenantRequest("suspended", "ToS violation")
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Channel channel = await db.Channels.SingleAsync(c => c.Id == tenant);
        channel.Status.Should().Be(AuthEnums.ChannelStatus.Suspended);
        channel.SuspendedAt.Should().Be(Now.UtcDateTime);
        channel.SuspendedReason.Should().Be("ToS violation");

        // The authorize call wrote the tenant-targeted audit row (SaaS mode — principals exist).
        IamAuditLog audit = await db.IamAuditLogs.SingleAsync(a =>
            a.Permission == "tenant:suspend"
        );
        audit.TargetBroadcasterId.Should().Be(tenant);
        audit.Justification.Should().Be("ToS violation");
        audit.Outcome.Should().Be(IamOutcome.Allowed);

        TenantSuspensionChangedEvent published = bus
            .Published.OfType<TenantSuspensionChangedEvent>()
            .Single();
        published.TargetBroadcasterId.Should().Be(tenant);
        published.NewStatus.Should().Be("suspended");
    }

    [Fact]
    public async Task Suspend_rejects_an_invalid_status_and_requires_the_permission()
    {
        (PlatformAdminService sut, AuthDbContext db, _) = Build();
        Guid unpermitted = SeedPrincipal(db, "tenant:read"); // holds a key, but not tenant:suspend
        Guid tenant = SeedTenant(db);
        await db.SaveChangesAsync();

        (
            await sut.SuspendTenantAsync(
                unpermitted,
                tenant,
                new SuspendTenantRequest("active", "nope")
            )
        )
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");

        Result denied = await sut.SuspendTenantAsync(
            unpermitted,
            tenant,
            new SuspendTenantRequest("suspended", "nope")
        );
        denied.ErrorCode.Should().Be("FORBIDDEN");
        (await db.Channels.SingleAsync(c => c.Id == tenant)).Status.Should().Be("active");
        // The denial itself was audited.
        (await db.IamAuditLogs.SingleAsync(a => a.Permission == "tenant:suspend"))
            .Outcome.Should()
            .Be(IamOutcome.Denied);
    }

    [Fact]
    public async Task Reinstate_restores_active_and_clears_the_suspension_fields()
    {
        (PlatformAdminService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid principal = SeedPrincipal(db, "tenant:suspend");
        Guid tenant = SeedTenant(db);
        await db.SaveChangesAsync();
        await sut.SuspendTenantAsync(
            principal,
            tenant,
            new SuspendTenantRequest("platform_banned", "spam")
        );

        Result result = await sut.ReinstateTenantAsync(principal, tenant, "appeal accepted");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Channel channel = await db.Channels.SingleAsync(c => c.Id == tenant);
        channel.Status.Should().Be(AuthEnums.ChannelStatus.Active);
        channel.SuspendedAt.Should().BeNull();
        channel.SuspendedReason.Should().BeNull();
        bus.Published.OfType<TenantSuspensionChangedEvent>().Last().NewStatus.Should().Be("active");
    }

    [Fact]
    public async Task BeginTenantAccess_creates_a_scoped_timeboxed_support_assignment()
    {
        (PlatformAdminService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid principal = SeedPrincipal(db, "tenant:access");
        Guid tenant = SeedTenant(db);
        db.IamRoles.Add(new IamRole { Id = Guid.NewGuid(), Name = "platform-support" });
        await db.SaveChangesAsync();
        DateTime expires = Now.UtcDateTime.AddHours(4);

        Result<TenantAccessGrantDto> result = await sut.BeginTenantAccessAsync(
            principal,
            tenant,
            new BeginTenantAccessRequest("support ticket 99", BreakGlass: false, expires)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.TargetBroadcasterId.Should().Be(tenant);
        result.Value.ExpiresAt.Should().Be(expires);

        // The grant IS a role assignment: platform-support, narrowed to the tenant, time-boxed.
        IamRoleAssignment assignment = await db.IamRoleAssignments.SingleAsync(a =>
            a.Id == result.Value.Id
        );
        assignment.PrincipalId.Should().Be(principal);
        assignment.ScopeChannelId.Should().Be(tenant);
        assignment.ExpiresAt.Should().Be(expires);
        assignment.Reason.Should().Be("support ticket 99");

        bus.Published.OfType<TenantAccessGrantedEvent>()
            .Single()
            .AccessGrantId.Should()
            .Be(result.Value.Id);
    }

    [Fact]
    public async Task BeginTenantAccess_requires_a_justification()
    {
        (PlatformAdminService sut, AuthDbContext db, _) = Build();
        Guid principal = SeedPrincipal(db, "tenant:access");
        Guid tenant = SeedTenant(db);
        await db.SaveChangesAsync();

        Result<TenantAccessGrantDto> result = await sut.BeginTenantAccessAsync(
            principal,
            tenant,
            new BeginTenantAccessRequest("  ", BreakGlass: false, null)
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task EndTenantAccess_revokes_own_grant_and_rejects_a_foreign_one()
    {
        (PlatformAdminService sut, AuthDbContext db, _) = Build();
        Guid principal = SeedPrincipal(db, "tenant:access");
        Guid other = SeedPrincipal(db, "tenant:access");
        Guid tenant = SeedTenant(db);
        db.IamRoles.Add(new IamRole { Id = Guid.NewGuid(), Name = "platform-support" });
        await db.SaveChangesAsync();
        Result<TenantAccessGrantDto> grant = await sut.BeginTenantAccessAsync(
            principal,
            tenant,
            new BeginTenantAccessRequest("ticket", BreakGlass: false, null)
        );

        // Someone else cannot end my grant.
        (await sut.EndTenantAccessAsync(other, grant.Value.Id))
            .ErrorCode.Should()
            .Be("NOT_FOUND");

        // I can — and it is revoked in the store.
        (await sut.EndTenantAccessAsync(principal, grant.Value.Id))
            .IsSuccess.Should()
            .BeTrue();
        (await db.IamRoleAssignments.SingleAsync(a => a.Id == grant.Value.Id))
            .RevokedAt.Should()
            .Be(Now.UtcDateTime);

        // Ending it twice is NOT_FOUND (no longer active).
        (await sut.EndTenantAccessAsync(principal, grant.Value.Id))
            .ErrorCode.Should()
            .Be("NOT_FOUND");
    }

    [Fact]
    public async Task ListTenants_filters_by_status_and_GetTenant_returns_the_detail()
    {
        (PlatformAdminService sut, AuthDbContext db, _) = Build();
        Guid principal = SeedPrincipal(db, "tenant:read", "tenant:suspend");
        Guid active = SeedTenant(db, "active_chan");
        Guid banned = SeedTenant(db, "banned_chan");
        await db.SaveChangesAsync();
        await sut.SuspendTenantAsync(
            principal,
            banned,
            new SuspendTenantRequest("platform_banned", "spam")
        );

        Result<PagedList<AdminTenantDto>> suspendedOnly = await sut.ListTenantsAsync(
            principal,
            new AdminTenantQuery(null, "platform_banned", null),
            Page
        );
        suspendedOnly.Value.Items.Should().ContainSingle();
        suspendedOnly.Value.Items[0].Id.Should().Be(banned);

        Result<AdminTenantDetailDto> detail = await sut.GetTenantAsync(principal, active);
        detail.IsSuccess.Should().BeTrue(detail.ErrorMessage);
        detail.Value.Name.Should().Be("active_chan");
        detail.Value.Status.Should().Be("active");
        detail.Value.OwnerDisplayName.Should().Be("active_chan");
    }

    [Fact]
    public async Task SearchAudit_filters_by_permission_and_target()
    {
        (PlatformAdminService sut, AuthDbContext db, _) = Build();
        Guid principal = SeedPrincipal(db, "audit:read", "tenant:suspend");
        Guid tenant = SeedTenant(db);
        await db.SaveChangesAsync();
        // Produce real audit rows through the real path.
        await sut.SuspendTenantAsync(principal, tenant, new SuspendTenantRequest("suspended", "x"));

        Result<PagedList<IamAuditEntryDto>> result = await sut.SearchAuditAsync(
            principal,
            new AuditSearchQuery(null, tenant, "tenant:suspend", null, null, null),
            Page
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].Permission.Should().Be("tenant:suspend");
        result.Value.Items[0].TargetBroadcasterId.Should().Be(tenant);
        result.Value.Items[0].Outcome.Should().Be("Allowed");
    }

    [Fact]
    public async Task StartImpersonation_mints_a_target_scoped_token_that_never_leaks_the_operators_admin_role()
    {
        (PlatformAdminService sut, AuthDbContext db, _) = Build();

        // A platform-marked admin (whose OWN login would carry the `admin` role) acting as a plain viewer.
        Guid adminUserId = SeedUser(db, "operator", isPlatformPrincipal: true);
        Guid principal = SeedPrincipalFor(db, adminUserId, "operator", "user:impersonate");
        Guid targetUserId = SeedUser(db, "viewer", isPlatformPrincipal: false);
        await db.SaveChangesAsync();

        Result<ImpersonationTokenDto> result = await sut.StartImpersonationAsync(
            principal,
            targetUserId,
            "repro ticket 42"
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.User.Id.Should().Be(targetUserId.ToString());

        // Decode: identity + roles are the TARGET's.
        JwtTokenService verifier = Jwt();
        ClaimsPrincipal token = verifier.ValidateAccessToken(result.Value.AccessToken)!;
        token.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be(targetUserId.ToString());
        List<string> roles = token.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        roles.Should().ContainSingle().Which.Should().Be("user");
        roles
            .Should()
            .NotContain(
                "admin",
                "the operator's admin role must never ride an impersonation token"
            );

        // The operator is named ONLY on the non-authoritative `act` claim (read raw — no auth path reads it).
        JwtSecurityToken raw = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
        raw.Claims.Should()
            .ContainSingle(c => c.Type == JwtTokenService.ActorClaim)
            .Which.Value.Should()
            .Be(adminUserId.ToString());

        // The returned expiry is the token's own `exp` — an access-only token (no refresh minted).
        result.Value.ExpiresAt.Should().Be(raw.ValidTo);

        // The audited authorize named WHO was impersonated on the append-only log.
        IamAuditLog audit = await db.IamAuditLogs.SingleAsync(a =>
            a.Permission == "user:impersonate"
        );
        audit.TargetResource.Should().Be(targetUserId.ToString());
        audit.Justification.Should().Be("repro ticket 42");
        audit.Outcome.Should().Be(IamOutcome.Allowed);
    }

    [Fact]
    public async Task StartImpersonation_carries_the_targets_admin_role_when_the_target_is_itself_an_admin()
    {
        (PlatformAdminService sut, AuthDbContext db, _) = Build();
        Guid adminUserId = SeedUser(db, "operator", isPlatformPrincipal: true);
        Guid principal = SeedPrincipalFor(db, adminUserId, "operator", "user:impersonate");
        // The TARGET is itself a platform principal — the token must reflect the TARGET's roles, so `admin` IS present.
        Guid targetAdminId = SeedUser(db, "coadmin", isPlatformPrincipal: true);
        await db.SaveChangesAsync();

        Result<ImpersonationTokenDto> result = await sut.StartImpersonationAsync(
            principal,
            targetAdminId,
            "audit the co-admin"
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        JwtTokenService verifier = Jwt();
        ClaimsPrincipal token = verifier.ValidateAccessToken(result.Value.AccessToken)!;
        token
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Should()
            .BeEquivalentTo(["user", "admin"]);
    }

    [Fact]
    public async Task StartImpersonation_without_the_permission_is_forbidden_audited_and_mints_no_token()
    {
        (PlatformAdminService sut, AuthDbContext db, _) = Build();
        Guid unpermitted = SeedPrincipal(db, "tenant:read"); // holds a key, but not user:impersonate
        Guid targetUserId = SeedUser(db, "viewer", isPlatformPrincipal: false);
        await db.SaveChangesAsync();

        Result<ImpersonationTokenDto> denied = await sut.StartImpersonationAsync(
            unpermitted,
            targetUserId,
            "no key"
        );

        denied.IsFailure.Should().BeTrue();
        denied.ErrorCode.Should().Be("FORBIDDEN");
        (await db.IamAuditLogs.SingleAsync(a => a.Permission == "user:impersonate"))
            .Outcome.Should()
            .Be(IamOutcome.Denied);
    }

    [Fact]
    public async Task StartImpersonation_requires_a_justification_and_a_known_target()
    {
        (PlatformAdminService sut, AuthDbContext db, _) = Build();
        Guid principal = SeedPrincipal(db, "user:impersonate");
        Guid targetUserId = SeedUser(db, "viewer", isPlatformPrincipal: false);
        await db.SaveChangesAsync();

        (await sut.StartImpersonationAsync(principal, targetUserId, "   "))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");

        (await sut.StartImpersonationAsync(principal, Guid.NewGuid(), "unknown target"))
            .ErrorCode.Should()
            .Be("NOT_FOUND");
    }
}
