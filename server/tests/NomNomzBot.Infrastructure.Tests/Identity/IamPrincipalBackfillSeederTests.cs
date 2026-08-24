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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the startup backfill for the D2 bootstrap gap: a <c>User</c> with <c>IsPlatformPrincipal = true</c>
/// that was promoted BEFORE <c>AuthService.MintPlatformOwnerPrincipalAsync</c> started minting a real
/// <c>IamPrincipal</c> is left with the marker but no principal row — <c>IamCallerPrincipalResolverService</c>
/// then correctly refuses every IAM-gated action for that caller ("No IAM principal is registered for this
/// caller."). <see cref="IamPrincipalBackfillSeeder"/> closes that gap for every already-promoted account,
/// idempotently, reusing <see cref="PlatformOwnerPrincipalMinter"/> — the exact row-building
/// <c>AuthService</c> uses at promotion time — so the grant is attributed to the same
/// <c>system-bootstrap</c> service-account principal.
/// </summary>
public sealed class IamPrincipalBackfillSeederTests
{
    private static IamPrincipalBackfillSeeder Build(AuthDbContext db) =>
        new(
            db,
            new PlatformOwnerPrincipalMinter(db),
            NullLogger<IamPrincipalBackfillSeeder>.Instance
        );

    private static void SeedOwnerRole(AuthDbContext db, Guid roleId) =>
        db.IamRoles.Add(
            new()
            {
                Id = roleId,
                Name = "platform-super-admin",
                IsSystem = true,
            }
        );

    private static User NewUser(string name, bool isPlatformPrincipal) =>
        new()
        {
            Username = name,
            UsernameNormalized = name,
            DisplayName = name,
            IsPlatformPrincipal = isPlatformPrincipal,
        };

    [Fact]
    public async Task Backfills_a_missing_principal_and_owner_role_for_a_pre_existing_platform_principal()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid roleId = Guid.NewGuid();
        SeedOwnerRole(db, roleId);
        User orphan = NewUser("stoney_eagle", isPlatformPrincipal: true);
        db.Users.Add(orphan);
        await db.SaveChangesAsync();

        await Build(db).SeedAsync();
        await db.SaveChangesAsync();

        IamPrincipal principal = db.IamPrincipals.Single(p => p.UserId == orphan.Id);
        principal.PrincipalType.Should().Be(IamPrincipalType.Employee);
        principal.IsActive.Should().BeTrue();

        IamRoleAssignment assignment = db.IamRoleAssignments.Single(a =>
            a.PrincipalId == principal.Id && a.RoleId == roleId
        );
        assignment.RevokedAt.Should().BeNull();
        assignment.AssignedByPrincipalId.Should().NotBe(Guid.Empty);

        IamPrincipal actor = db.IamPrincipals.Single(p => p.Id == assignment.AssignedByPrincipalId);
        actor.PrincipalType.Should().Be(IamPrincipalType.ServiceAccount);
        actor.Name.Should().Be("system-bootstrap");
    }

    [Fact]
    public async Task Leaves_a_user_who_already_has_a_principal_untouched()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid roleId = Guid.NewGuid();
        SeedOwnerRole(db, roleId);
        User owner = NewUser("qtkitte", isPlatformPrincipal: true);
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        IamPrincipal existing = new()
        {
            PrincipalType = IamPrincipalType.Employee,
            UserId = owner.Id,
            Name = "Platform Owner",
            IsActive = true,
        };
        db.IamPrincipals.Add(existing);
        await db.SaveChangesAsync();
        Guid existingPrincipalId = existing.Id;
        int principalCountBefore = await db.IamPrincipals.CountAsync();
        int assignmentCountBefore = await db.IamRoleAssignments.CountAsync();

        await Build(db).SeedAsync();
        await db.SaveChangesAsync();

        (await db.IamPrincipals.CountAsync()).Should().Be(principalCountBefore);
        (await db.IamRoleAssignments.CountAsync()).Should().Be(assignmentCountBefore);
        db.IamPrincipals.Single(p => p.UserId == owner.Id).Id.Should().Be(existingPrincipalId);
    }

    [Fact]
    public async Task Never_creates_a_principal_for_a_non_platform_principal_user()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid roleId = Guid.NewGuid();
        SeedOwnerRole(db, roleId);
        User viewer = NewUser("aaoa_", isPlatformPrincipal: false);
        db.Users.Add(viewer);
        await db.SaveChangesAsync();

        await Build(db).SeedAsync();
        await db.SaveChangesAsync();

        db.IamPrincipals.Any(p => p.UserId == viewer.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Running_the_backfill_twice_is_a_no_op_the_second_time()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid roleId = Guid.NewGuid();
        SeedOwnerRole(db, roleId);
        User first = NewUser("stoney_eagle", isPlatformPrincipal: true);
        User second = NewUser("qtkitte", isPlatformPrincipal: true);
        db.Users.AddRange(first, second);
        await db.SaveChangesAsync();

        await Build(db).SeedAsync();
        await db.SaveChangesAsync();
        int principalCountAfterFirstRun = await db.IamPrincipals.CountAsync();
        int assignmentCountAfterFirstRun = await db.IamRoleAssignments.CountAsync();

        await Build(db).SeedAsync();
        await db.SaveChangesAsync();

        (await db.IamPrincipals.CountAsync()).Should().Be(principalCountAfterFirstRun);
        (await db.IamRoleAssignments.CountAsync()).Should().Be(assignmentCountAfterFirstRun);
        // Both real users are minted — proves the multi-user pass shares one system-bootstrap actor rather
        // than minting a duplicate service account per orphaned user in the same run.
        db.IamPrincipals.Count(p => p.PrincipalType == IamPrincipalType.ServiceAccount)
            .Should()
            .Be(1);
        db.IamPrincipals.Count(p => p.UserId == first.Id || p.UserId == second.Id).Should().Be(2);
    }
}
