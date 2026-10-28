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

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Authentication;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Infrastructure.Platform.Auth;

namespace NomNomzBot.Api.Tests.Authentication;

/// <summary>
/// Proves the S098b end-to-end contract at the logic level exercised by the JwtBearer
/// <c>OnTokenValidated</c> handler in Program.cs: a token whose <c>sid</c> claim maps to a revoked
/// session fails the check the moment the session is revoked — the SAME token, previously accepted,
/// is now rejected — without needing the token itself to expire or be re-minted.
/// </summary>
public class SessionRevocationCheckTests
{
    private static ClaimsPrincipal PrincipalWithSid(Guid sessionId) =>
        new(new ClaimsIdentity([new(JwtTokenService.SessionClaim, sessionId.ToString())]));

    private static SessionRevocationService CreateRevocationService()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { { "Jwt:ExpiryMinutes", "60" } }
            )
            .Build();
        return new(
            new NomNomzBot.Infrastructure.Platform.Caching.MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                Microsoft
                    .Extensions
                    .Logging
                    .Abstractions
                    .NullLogger<NomNomzBot.Infrastructure.Platform.Caching.MemoryCacheService>
                    .Instance
            ),
            new MemoryCache(new MemoryCacheOptions()),
            config
        );
    }

    [Fact]
    public async Task ValidToken_AuthenticatesBeforeRevocation()
    {
        ISessionRevocationService revocation = CreateRevocationService();
        Guid sessionId = Guid.NewGuid();
        ClaimsPrincipal principal = PrincipalWithSid(sessionId);

        bool isRevoked = await SessionRevocationCheck.IsSessionRevokedAsync(principal, revocation);

        isRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task SameToken_IsRejected_ImmediatelyAfterItsSessionIsRevoked()
    {
        ISessionRevocationService revocation = CreateRevocationService();
        Guid sessionId = Guid.NewGuid();
        ClaimsPrincipal principal = PrincipalWithSid(sessionId);

        (await SessionRevocationCheck.IsSessionRevokedAsync(principal, revocation))
            .Should()
            .BeFalse();

        await revocation.RevokeAsync(sessionId);

        (await SessionRevocationCheck.IsSessionRevokedAsync(principal, revocation))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task RevokingADifferentSession_DoesNotAffectThisToken()
    {
        ISessionRevocationService revocation = CreateRevocationService();
        Guid sessionId = Guid.NewGuid();
        ClaimsPrincipal principal = PrincipalWithSid(sessionId);

        await revocation.RevokeAsync(Guid.NewGuid());

        (await SessionRevocationCheck.IsSessionRevokedAsync(principal, revocation))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task MissingSidClaim_IsTreatedAsNotRevoked()
    {
        ISessionRevocationService revocation = CreateRevocationService();
        ClaimsPrincipal principal = new(new ClaimsIdentity());

        (await SessionRevocationCheck.IsSessionRevokedAsync(principal, revocation))
            .Should()
            .BeFalse();
    }
}
