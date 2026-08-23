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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves fix D2 item 4: the shared acting-principal resolver DENIES on every failure path instead of the
/// old duplicated controller helper's behaviour of folding the failure into <see cref="Guid.Empty"/> — which
/// the service's self-host short-circuit then treated as an implicit ALLOW. Both a malformed/missing user id
/// claim and a claim with no backing <c>IamPrincipal</c> row must deny; only a real principal resolves.
/// </summary>
public sealed class IamCallerPrincipalResolverServiceTests
{
    [Fact]
    public async Task A_malformed_user_id_claim_is_denied()
    {
        IPlatformIamService iam = Substitute.For<IPlatformIamService>();
        IamCallerPrincipalResolverService sut = new(iam);

        Result<Guid> result = await sut.ResolveActingPrincipalIdAsync("not-a-guid");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        await iam.DidNotReceiveWithAnyArgs().ResolvePrincipalAsync(default, default);
    }

    [Fact]
    public async Task A_null_user_id_claim_is_denied()
    {
        IPlatformIamService iam = Substitute.For<IPlatformIamService>();
        IamCallerPrincipalResolverService sut = new(iam);

        Result<Guid> result = await sut.ResolveActingPrincipalIdAsync(null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    /// <summary>
    /// The regression this fix closes: no principal row for an otherwise-valid caller must DENY, never
    /// resolve to <see cref="Guid.Empty"/> (which the duplicated controller helper used to return, and which
    /// the service's self-host short-circuit would then treat as full access).
    /// </summary>
    [Fact]
    public async Task No_principal_row_for_a_valid_user_id_is_denied_not_empty_guid()
    {
        Guid userId = Guid.NewGuid();
        IPlatformIamService iam = Substitute.For<IPlatformIamService>();
        iam.ResolvePrincipalAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IamPrincipalDto?>(null));
        IamCallerPrincipalResolverService sut = new(iam);

        Result<Guid> result = await sut.ResolveActingPrincipalIdAsync(userId.ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task A_resolvable_principal_succeeds_with_its_id()
    {
        Guid userId = Guid.NewGuid();
        Guid principalId = Guid.NewGuid();
        IPlatformIamService iam = Substitute.For<IPlatformIamService>();
        iam.ResolvePrincipalAsync(userId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IamPrincipalDto?>(
                    new(principalId, IamPrincipalType.Employee, userId, "owner", true, null, null)
                )
            );
        IamCallerPrincipalResolverService sut = new(iam);

        Result<Guid> result = await sut.ResolveActingPrincipalIdAsync(userId.ToString());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(principalId);
    }
}
