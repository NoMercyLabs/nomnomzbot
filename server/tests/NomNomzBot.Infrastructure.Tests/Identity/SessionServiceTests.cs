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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Auth;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the session + rotating-refresh-token behavior (identity-auth §3.3): a session issues a hashed
/// (never plaintext) refresh token; rotation consumes the old token and issues a chained successor; and
/// presenting a consumed token is detected as reuse — the lineage is revoked and a reuse event fires.
/// </summary>
public sealed class SessionServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-00000000aa01");

    private static (SessionService Service, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = "super-secret-key-that-is-at-least-32-bytes-long!",
                    ["Jwt:Issuer"] = "nomnomzbot",
                    ["Jwt:Audience"] = "nomnomzbot",
                }
            )
            .Build();
        JwtTokenService jwt = new(config, TimeProvider.System);
        SessionService service = new(
            db,
            jwt,
            bus,
            TimeProvider.System,
            NullLogger<SessionService>.Instance
        );
        return (service, db, bus);
    }

    private static async Task<User> SeedUserAsync(AuthDbContext db)
    {
        User user = new()
        {
            TwitchUserId = "u-1",
            Username = "streamer",
            UsernameNormalized = "streamer",
            DisplayName = "Streamer",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static AuthContextDto Ctx() => new(AuthEnums.ClientType.Web, "1.2.3.4", "agent");

    [Fact]
    public async Task CreateSession_PersistsHashedRefreshToken_AndReturnsRawOnce()
    {
        (SessionService service, AuthDbContext db, RecordingEventBus bus) = Build();
        User user = await SeedUserAsync(db);

        Result<SessionTokensDto> result = await service.CreateSessionAsync(user.Id, Tenant, Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Split('.').Should().HaveCount(3);
        result.Value.RawRefreshToken.Should().NotBeNullOrEmpty();

        RefreshToken stored = await db.RefreshTokens.AsNoTracking().SingleAsync();
        // The raw value is never persisted — only its hash.
        stored.TokenHash.Should().NotBe(result.Value.RawRefreshToken);
        stored.TokenHash.Should().HaveLength(64);
        stored.ConsumedAt.Should().BeNull();

        bus.Published.OfType<UserLoggedInEvent>().Single().UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Rotate_ConsumesCurrent_AndIssuesChainedSuccessor()
    {
        (SessionService service, AuthDbContext db, _) = Build();
        User user = await SeedUserAsync(db);
        SessionTokensDto first = (await service.CreateSessionAsync(user.Id, Tenant, Ctx())).Value;

        Result<SessionTokensDto> rotated = await service.RotateAsync(first.RawRefreshToken, Ctx());

        rotated.IsSuccess.Should().BeTrue();
        rotated.Value.RawRefreshToken.Should().NotBe(first.RawRefreshToken);
        rotated.Value.SessionId.Should().Be(first.SessionId);

        List<RefreshToken> tokens = await db.RefreshTokens.AsNoTracking().ToListAsync();
        tokens.Should().HaveCount(2);
        tokens.Should().Contain(t => t.ConsumedAt != null, "the presented token is consumed");
        tokens
            .Should()
            .Contain(t => t.PreviousTokenHash != null, "the successor links to its predecessor");
    }

    [Fact]
    public async Task Rotate_WithConsumedToken_IsReuse_RevokesLineage_AndEmitsEvent()
    {
        (SessionService service, AuthDbContext db, RecordingEventBus bus) = Build();
        User user = await SeedUserAsync(db);
        SessionTokensDto first = (await service.CreateSessionAsync(user.Id, Tenant, Ctx())).Value;

        // Legitimate rotation consumes `first`.
        await service.RotateAsync(first.RawRefreshToken, Ctx());

        // Replaying the now-consumed `first` is reuse — must fail closed.
        Result<SessionTokensDto> reuse = await service.RotateAsync(first.RawRefreshToken, Ctx());

        reuse.ErrorCode.Should().Be("TOKEN_REUSE");
        bus.Published.OfType<RefreshTokenReuseDetectedEvent>().Should().ContainSingle();

        // The whole session lineage is revoked.
        AuthSession session = await db.AuthSessions.AsNoTracking().SingleAsync();
        session.RevokedAt.Should().NotBeNull();
        List<RefreshToken> tokens = await db.RefreshTokens.AsNoTracking().ToListAsync();
        tokens.Should().OnlyContain(t => t.RevokedAt != null || t.ConsumedAt != null);
    }

    [Fact]
    public async Task RevokeAllForUser_RevokesEverySessionAndToken()
    {
        (SessionService service, AuthDbContext db, _) = Build();
        User user = await SeedUserAsync(db);
        await service.CreateSessionAsync(user.Id, Tenant, Ctx());
        await service.CreateSessionAsync(user.Id, Tenant, Ctx());

        Result<int> revoked = await service.RevokeAllForUserAsync(
            user.Id,
            AuthEnums.RefreshTokenRevokedReason.Logout
        );

        revoked.Value.Should().Be(2);
        (await db.AuthSessions.AsNoTracking().ToListAsync())
            .Should()
            .OnlyContain(s => s.RevokedAt != null);
        (await db.RefreshTokens.AsNoTracking().ToListAsync())
            .Should()
            .OnlyContain(t => t.RevokedAt != null);
    }
}
