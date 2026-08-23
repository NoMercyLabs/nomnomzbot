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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the D2 bootstrap fix: promoting a user to platform owner mints a REAL <c>IamPrincipal</c> +
/// <c>platform-super-admin</c> role assignment (not just the <c>IsPlatformPrincipal</c> marker
/// <see cref="AdminBootstrap"/> alone leaves behind), the mint is idempotent across repeated bootstrap runs,
/// and every bootstrap-time role assignment is attributed to a real system principal — never
/// <see cref="Guid.Empty"/>. Exercises <see cref="AuthService.MintPlatformOwnerPrincipalAsync"/> directly
/// (internal, via <c>InternalsVisibleTo</c>) so the proof does not have to drive the full Twitch login flow.
/// </summary>
public sealed class AuthServiceBootstrapPrincipalTests
{
    private static readonly Guid OwnerRoleId = Guid.NewGuid();

    [Fact]
    public async Task Mint_creates_a_real_principal_and_owner_role_assignment()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        SeedOwnerRole(db);
        User owner = NewUser("owner");
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        AuthService sut = Build(db);

        await sut.MintPlatformOwnerPrincipalAsync(owner.Id, CancellationToken.None);
        await db.SaveChangesAsync();

        IamPrincipal principal = db.IamPrincipals.Single(p => p.UserId == owner.Id);
        principal.PrincipalType.Should().Be(IamPrincipalType.Employee);
        principal.IsActive.Should().BeTrue();
        IamRoleAssignment assignment = db.IamRoleAssignments.Single(a =>
            a.PrincipalId == principal.Id && a.RoleId == OwnerRoleId
        );
        assignment.RevokedAt.Should().BeNull();
    }

    /// <summary>
    /// Fix D2 item 3: the acting principal on the bootstrap-minted assignment must be a real system
    /// principal, never <see cref="Guid.Empty"/> — otherwise the audit trail attributes the grant to nobody.
    /// </summary>
    [Fact]
    public async Task Mint_attributes_the_assignment_to_a_real_system_principal_never_empty()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        SeedOwnerRole(db);
        User owner = NewUser("owner");
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        AuthService sut = Build(db);

        await sut.MintPlatformOwnerPrincipalAsync(owner.Id, CancellationToken.None);
        await db.SaveChangesAsync();

        IamPrincipal principal = db.IamPrincipals.Single(p => p.UserId == owner.Id);
        IamRoleAssignment assignment = db.IamRoleAssignments.Single(a =>
            a.PrincipalId == principal.Id
        );
        assignment.AssignedByPrincipalId.Should().NotBeNull();
        assignment.AssignedByPrincipalId.Should().NotBe(Guid.Empty);
        // The attributed principal must itself be a real, resolvable row.
        db.IamPrincipals.Should()
            .Contain(p =>
                p.Id == assignment.AssignedByPrincipalId
                && p.PrincipalType == IamPrincipalType.ServiceAccount
            );
    }

    /// <summary>
    /// Bootstrap must be safe to re-run (a subsequent login, a restart mid-onboarding) without ever
    /// duplicating the owner's principal or role assignment.
    /// </summary>
    [Fact]
    public async Task Running_bootstrap_twice_produces_exactly_one_principal_and_one_assignment()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        SeedOwnerRole(db);
        User owner = NewUser("owner");
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        AuthService sut = Build(db);

        await sut.MintPlatformOwnerPrincipalAsync(owner.Id, CancellationToken.None);
        await db.SaveChangesAsync();
        await sut.MintPlatformOwnerPrincipalAsync(owner.Id, CancellationToken.None);
        await db.SaveChangesAsync();

        db.IamPrincipals.Count(p => p.UserId == owner.Id).Should().Be(1);
        Guid principalId = db.IamPrincipals.Single(p => p.UserId == owner.Id).Id;
        db.IamRoleAssignments.Count(a =>
                a.PrincipalId == principalId && a.RoleId == OwnerRoleId && a.RevokedAt == null
            )
            .Should()
            .Be(1);
        // The system principal attributed as the actor is also not duplicated across runs.
        db.IamPrincipals.Count(p => p.PrincipalType == IamPrincipalType.ServiceAccount)
            .Should()
            .Be(1);
    }

    private static void SeedOwnerRole(AuthDbContext db) =>
        db.IamRoles.Add(
            new()
            {
                Id = OwnerRoleId,
                Name = "platform-super-admin",
                IsSystem = true,
            }
        );

    private static User NewUser(string name) =>
        new()
        {
            Username = name,
            UsernameNormalized = name,
            DisplayName = name,
        };

    private static AuthService Build(AuthDbContext db)
    {
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Twitch:ClientId"] = "public-id" }
            )
            .Build();
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );

        return new(
            db,
            Substitute.For<ITwitchAuthService>(),
            Substitute.For<ITwitchDeviceCodeService>(),
            Substitute.For<IIntegrationTokenVault>(),
            Substitute.For<ISessionService>(),
            new RecordingEventBus(),
            credentials,
            Substitute.For<IHttpClientFactory>(),
            config,
            new(DeploymentMode.SelfHostLite),
            TimeProvider.System,
            new(),
            NullLogger<AuthService>.Instance
        );
    }
}
