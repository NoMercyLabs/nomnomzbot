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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves <see cref="IUserService.GetCurrentUserAsync"/> wire-encodes its <c>Id</c> the same way every other
/// owned-id field in the API does (<see cref="OwnedIdCodec"/>). Before this fix the DTO's <c>Id</c> property
/// was typed <c>string</c> and built with a bare <c>u.Id.ToString()</c>, which bypasses
/// <c>UlidGuidJsonConverter</c> (that converter only runs for <c>Guid</c>-typed properties) and leaked the raw
/// hyphenated Guid. Every OTHER caller-identity field the dashboard receives (e.g. an IAM principal's
/// <c>Guid? UserId</c>) IS Guid-typed and therefore ULID-encoded, so a client-side <c>==</c> comparison
/// between "my own id" and "this IAM principal's id" never matched — silently denying every genuine
/// content:read/author/publish holder the Admin → Content tab's self-permission gate (found driving S-UX-4
/// verification against the real deployed admin plane, 2026-09-12).
/// </summary>
public sealed class UserServiceGetCurrentUserTests
{
    private static (IUserService Svc, AuthDbContext Db) Build(Guid userId)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId.ToString());
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return (AuthTestBuilder.UserService(db, currentUser, scopeFactory), db);
    }

    [Fact]
    public async Task GetCurrentUserAsync_encodes_the_id_as_a_ulid_not_a_raw_guid()
    {
        Guid userId = Guid.NewGuid();
        (IUserService svc, AuthDbContext db) = Build(userId);
        db.Users.Add(
            new User
            {
                Id = userId,
                Username = "stoney_eagle",
                UsernameNormalized = "stoney_eagle",
                DisplayName = "Stoney_Eagle",
                BroadcasterType = "affiliate",
                Enabled = true,
            }
        );
        await db.SaveChangesAsync();

        Result<CurrentUserDto> result = await svc.GetCurrentUserAsync();

        result.IsSuccess.Should().BeTrue();
        // The raw hyphenated form must be GONE — that was the bug — and the wire form must decode back to
        // the exact same underlying Guid, so nothing downstream that resolves it loses fidelity.
        result.Value.Id.Should().NotBe(userId.ToString());
        result.Value.Id.Should().Be(OwnedIdCodec.Encode(userId));
        OwnedIdCodec.TryDecode(result.Value.Id, out Guid decoded).Should().BeTrue();
        decoded.Should().Be(userId);
    }
}
