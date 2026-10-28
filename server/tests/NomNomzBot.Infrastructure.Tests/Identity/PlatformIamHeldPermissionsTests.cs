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

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the signed-in person can read their OWN platform permission keys: <c>auth/me</c> carries them so the
/// dashboard gates admin tabs and buttons on the finer keys (iam:manage, billing:write, ...), not just on the
/// coarse <c>IsAdmin</c> flag. Self-host is implicitly full; SaaS reports exactly the keys the user's active
/// principal holds; a person without the platform-principal marker never touches the IAM plane.
/// </summary>
public sealed class PlatformIamHeldPermissionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static (PlatformIamService Iam, AuthDbContext Db) BuildIam(DeploymentMode mode)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        PlatformIamService iam = new(
            db,
            new RecordingEventBus(),
            new FakeTimeProvider(Now),
            new(mode)
        );
        return (iam, db);
    }

    private static Guid SeedUser(AuthDbContext db, bool platformMarker)
    {
        Guid userId = Guid.NewGuid();
        db.Users.Add(
            new User
            {
                Id = userId,
                Username = "operator",
                UsernameNormalized = "operator",
                DisplayName = "Operator",
                BroadcasterType = "",
                Enabled = true,
                IsPlatformPrincipal = platformMarker,
            }
        );
        return userId;
    }

    private static void SeedPrincipalHolding(
        AuthDbContext db,
        Guid userId,
        bool active,
        params string[] keys
    )
    {
        Guid principalId = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new()
            {
                Id = principalId,
                UserId = userId,
                PrincipalType = IamPrincipalType.Employee,
                Name = "operator",
                IsActive = active,
            }
        );
        db.IamRoles.Add(new() { Id = roleId, Name = $"role-{roleId}" });
        foreach (string key in keys)
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
    }

    [Fact]
    public async Task Self_host_holds_every_catalog_key_sorted()
    {
        (PlatformIamService iam, AuthDbContext db) = BuildIam(DeploymentMode.SelfHostLite);
        Guid userId = SeedUser(db, platformMarker: true);
        await db.SaveChangesAsync();

        IReadOnlyList<string> held = await iam.GetHeldPermissionsForUserAsync(userId);

        held.Should().BeEquivalentTo(IamPermissionKeys.All);
        held.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public async Task Saas_holds_exactly_the_keys_of_the_active_principal()
    {
        (PlatformIamService iam, AuthDbContext db) = BuildIam(DeploymentMode.Saas);
        Guid userId = SeedUser(db, platformMarker: true);
        SeedPrincipalHolding(
            db,
            userId,
            active: true,
            IamPermissionKeys.IamManage,
            IamPermissionKeys.BillingRead
        );
        await db.SaveChangesAsync();

        IReadOnlyList<string> held = await iam.GetHeldPermissionsForUserAsync(userId);

        held.Should()
            .Equal(
                new[] { IamPermissionKeys.BillingRead, IamPermissionKeys.IamManage }.Order(
                    StringComparer.Ordinal
                )
            );
        held.Should().NotContain(IamPermissionKeys.BillingWrite);
    }

    [Fact]
    public async Task Saas_holds_nothing_without_a_principal_or_with_an_inactive_one()
    {
        (PlatformIamService iam, AuthDbContext db) = BuildIam(DeploymentMode.Saas);
        Guid withoutPrincipal = SeedUser(db, platformMarker: true);
        Guid inactive = SeedUser(db, platformMarker: true);
        SeedPrincipalHolding(db, inactive, active: false, IamPermissionKeys.IamManage);
        await db.SaveChangesAsync();

        (await iam.GetHeldPermissionsForUserAsync(withoutPrincipal)).Should().BeEmpty();
        (await iam.GetHeldPermissionsForUserAsync(inactive)).Should().BeEmpty();
    }

    private static async Task<CurrentUserDto> CurrentUserAsync(
        AuthDbContext db,
        PlatformIamService iam,
        Guid userId
    )
    {
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId.ToString());
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        IUserService svc = AuthTestBuilder.UserService(db, currentUser, scopeFactory, iam);

        Result<CurrentUserDto> result = await svc.GetCurrentUserAsync();

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Current_user_carries_the_platform_keys_of_a_marked_operator()
    {
        (PlatformIamService iam, AuthDbContext db) = BuildIam(DeploymentMode.Saas);
        Guid userId = SeedUser(db, platformMarker: true);
        SeedPrincipalHolding(db, userId, active: true, IamPermissionKeys.BillingWrite);
        await db.SaveChangesAsync();

        CurrentUserDto me = await CurrentUserAsync(db, iam, userId);

        me.IsAdmin.Should().BeTrue();
        me.PlatformPermissions.Should().Equal(IamPermissionKeys.BillingWrite);
    }

    [Fact]
    public async Task Current_user_without_the_platform_marker_gets_no_keys_even_on_self_host()
    {
        (PlatformIamService iam, AuthDbContext db) = BuildIam(DeploymentMode.SelfHostLite);
        Guid userId = SeedUser(db, platformMarker: false);
        await db.SaveChangesAsync();

        CurrentUserDto me = await CurrentUserAsync(db, iam, userId);

        me.IsAdmin.Should().BeFalse();
        me.PlatformPermissions.Should().BeEmpty();
    }
}
