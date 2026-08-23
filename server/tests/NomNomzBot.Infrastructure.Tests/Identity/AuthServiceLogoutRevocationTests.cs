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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the S098b logout seam: ending a session revokes its JWT <c>sid</c> claim too — a refresh-token
/// revoke alone leaves an already-issued, still-unexpired access token usable. <c>LogoutAsync</c> revokes
/// the one session ending; <c>LogoutAllAsync</c> revokes every session belonging to the user, not just the
/// count <c>ISessionService.RevokeAllForUserAsync</c> reports back.
/// </summary>
public sealed class AuthServiceLogoutRevocationTests
{
    [Fact]
    public async Task LogoutAsync_RevokesTheSessionsSid()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ISessionService sessions = Substitute.For<ISessionService>();
        sessions
            .RevokeSessionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        ISessionRevocationService revocation = Substitute.For<ISessionRevocationService>();
        AuthService sut = Build(db, sessions, revocation);

        Guid userId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();

        Result result = await sut.LogoutAsync(userId, sessionId);

        result.IsSuccess.Should().BeTrue();
        await revocation.Received(1).RevokeAsync(sessionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogoutAsync_SessionRevokeFails_DoesNotRevokeTheSid()
    {
        // If the refresh-token session revoke itself failed, nothing actually ended — the sid must stay
        // untouched rather than getting revoked on a no-op.
        AuthDbContext db = AuthTestBuilder.NewContext();
        ISessionService sessions = Substitute.For<ISessionService>();
        sessions
            .RevokeSessionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("not found", "NOT_FOUND"));
        ISessionRevocationService revocation = Substitute.For<ISessionRevocationService>();
        AuthService sut = Build(db, sessions, revocation);

        Result result = await sut.LogoutAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        await revocation.DidNotReceive().RevokeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogoutAllAsync_RevokesTheSidOfEveryActiveSessionForTheUser()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid userId = Guid.NewGuid();
        Guid sessionA = Guid.NewGuid();
        Guid sessionB = Guid.NewGuid();
        db.Users.Add(NewUser(userId, "alice"));
        db.AuthSessions.Add(NewSession(sessionA, userId));
        db.AuthSessions.Add(NewSession(sessionB, userId));
        db.AuthSessions.Add(NewSession(Guid.NewGuid(), Guid.NewGuid())); // a DIFFERENT user's session
        await db.SaveChangesAsync();

        ISessionService sessions = Substitute.For<ISessionService>();
        sessions
            .RevokeAllForUserAsync(userId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(2));
        ISessionRevocationService revocation = Substitute.For<ISessionRevocationService>();
        AuthService sut = Build(db, sessions, revocation);

        Result<int> result = await sut.LogoutAllAsync(userId);

        result.Value.Should().Be(2);
        await revocation.Received(1).RevokeAsync(sessionA, Arg.Any<CancellationToken>());
        await revocation.Received(1).RevokeAsync(sessionB, Arg.Any<CancellationToken>());
        await revocation.Received(2).RevokeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static User NewUser(Guid id, string username) =>
        new()
        {
            Id = id,
            Username = username,
            UsernameNormalized = username,
            DisplayName = username,
        };

    private static AuthSession NewSession(Guid id, Guid userId) =>
        new()
        {
            Id = id,
            UserId = userId,
            ClientType = "web",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        };

    private static AuthService Build(
        AuthDbContext db,
        ISessionService sessions,
        ISessionRevocationService revocation
    )
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
            sessions,
            revocation,
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
