// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Infrastructure.Platform.Auth;

namespace NomNomzBot.Infrastructure.Tests.Platform.Auth;

/// <summary>
/// Proves the access-JWT contract (identity-auth §3.2, §9): the HS256 default path mints a token carrying
/// the internal user Guid (<c>sub</c>), the resolved tenant (<c>tenant</c>) and session (<c>sid</c>) claims,
/// and validation accepts a good token but rejects a tampered signature, a wrong key/issuer/audience, and
/// an expired token. Opaque refresh-token values are random and non-JWT.
/// </summary>
public class JwtTokenServiceTests
{
    private static readonly Guid UserId = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly Guid TenantId = Guid.Parse("0192a000-0000-7000-8000-0000000000b2");
    private static readonly Guid SessionId = Guid.Parse("0192a000-0000-7000-8000-0000000000c3");

    private static JwtTokenService Create(
        string key = "super-secret-key-that-is-at-least-32-bytes-long!",
        string issuer = "TestIssuer",
        string audience = "TestAudience",
        string expirationMinutes = "60",
        TimeProvider? timeProvider = null
    )
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    { "Jwt:Secret", key },
                    { "Jwt:Issuer", issuer },
                    { "Jwt:Audience", audience },
                    { "Jwt:ExpiryMinutes", expirationMinutes },
                }
            )
            .Build();

        return new(config, timeProvider ?? TimeProvider.System);
    }

    // ─── GenerateAccessToken ──────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_ProducesAThreePartToken()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateAccessToken(UserId, "alice", TenantId, SessionId);

        token.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public void GenerateAccessToken_EmbedsSubTenantAndSessionClaims()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateAccessToken(UserId, "alice", TenantId, SessionId);

        ClaimsPrincipal principal = svc.ValidateAccessToken(token)!;

        principal.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be(UserId.ToString());
        principal.FindFirstValue(ClaimTypes.Name).Should().Be("alice");
        principal.FindFirstValue(JwtTokenService.TenantClaim).Should().Be(TenantId.ToString());
        principal.FindFirstValue(JwtTokenService.SessionClaim).Should().Be(SessionId.ToString());
    }

    [Fact]
    public void GenerateAccessToken_NoTenant_OmitsTenantClaim()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateAccessToken(UserId, "alice", broadcasterId: null, SessionId);

        ClaimsPrincipal principal = svc.ValidateAccessToken(token)!;
        principal.FindFirst(JwtTokenService.TenantClaim).Should().BeNull();
    }

    [Fact]
    public void GenerateAccessToken_WithRoles_EmbedsRoleClaims()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateAccessToken(
            UserId,
            "alice",
            TenantId,
            SessionId,
            ["user", "admin"]
        );

        ClaimsPrincipal principal = svc.ValidateAccessToken(token)!;
        principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Should().Contain(["user", "admin"]);
    }

    [Fact]
    public void GenerateAccessToken_WithIdp_EmbedsIdpClaim()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateAccessToken(
            UserId,
            "alice",
            TenantId,
            SessionId,
            roles: null,
            idp: "twitch"
        );

        // Assert the raw wire claim (ValidateAccessToken remaps the "idp" short name to a URI).
        System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt =
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().ContainSingle(c => c.Type == "idp").Which.Value.Should().Be("twitch");
    }

    [Fact]
    public void GenerateAccessToken_NoIdp_OmitsIdpClaim()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateAccessToken(UserId, "alice", TenantId, SessionId);

        System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt =
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == "idp");
    }

    [Fact]
    public void GenerateAccessToken_ForImpersonation_EmbedsActClaims_AndKeepsTheTargetsRoles()
    {
        JwtTokenService svc = Create();

        // Minted for a NON-admin TARGET (roles = ["user"]) while an admin operator acts as them.
        string token = svc.GenerateAccessToken(
            UserId,
            "target-user",
            TenantId,
            SessionId,
            roles: ["user"],
            idp: "twitch",
            actorUserId: "admin-operator-id",
            actorUsername: "operator"
        );

        // Read the raw wire claims (ValidateAccessToken may remap short names for role/sub).
        System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt =
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should()
            .ContainSingle(c => c.Type == JwtTokenService.ActorClaim)
            .Which.Value.Should()
            .Be("admin-operator-id");
        jwt.Claims.Should()
            .ContainSingle(c => c.Type == JwtTokenService.ActorNameClaim)
            .Which.Value.Should()
            .Be("operator");

        // The role claims are the TARGET's — the operator's `admin` role never leaks onto the token.
        ClaimsPrincipal principal = svc.ValidateAccessToken(token)!;
        List<string> roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        roles.Should().ContainSingle().Which.Should().Be("user");
        roles.Should().NotContain("admin");
    }

    [Fact]
    public void GenerateAccessToken_NoActor_OmitsActClaims()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateAccessToken(UserId, "alice", TenantId, SessionId);

        System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt =
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == JwtTokenService.ActorClaim);
        jwt.Claims.Should().NotContain(c => c.Type == JwtTokenService.ActorNameClaim);
    }

    [Fact]
    public void GenerateAccessToken_DistinctCalls_DifferByJti()
    {
        JwtTokenService svc = Create();
        string a = svc.GenerateAccessToken(UserId, "alice", TenantId, SessionId);
        string b = svc.GenerateAccessToken(UserId, "alice", TenantId, SessionId);

        a.Should().NotBe(b);
    }

    // ─── GenerateRefreshTokenValue ────────────────────────────────────────────

    [Fact]
    public void GenerateRefreshTokenValue_IsRandomAndNotAJwt()
    {
        JwtTokenService svc = Create();
        string a = svc.GenerateRefreshTokenValue();
        string b = svc.GenerateRefreshTokenValue();

        a.Should().NotBeNullOrEmpty();
        a.Should().NotBe(b, "refresh values are cryptographically random, not self-describing");
        a.Split('.').Should().NotHaveCount(3, "the refresh value is opaque, not a JWT");
    }

    // ─── ValidateAccessToken ──────────────────────────────────────────────────

    [Fact]
    public void ValidateAccessToken_GoodToken_ReturnsPrincipal()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateAccessToken(UserId, "alice", TenantId, SessionId);

        svc.ValidateAccessToken(token).Should().NotBeNull();
    }

    [Fact]
    public void ValidateAccessToken_TamperedSignature_ReturnsNull()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateAccessToken(UserId, "alice", TenantId, SessionId);
        string[] parts = token.Split('.');
        string tampered = $"{parts[0]}.{parts[1]}.INVALIDSIGNATURE";

        svc.ValidateAccessToken(tampered).Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_WrongKey_ReturnsNull()
    {
        JwtTokenService signer = Create(key: "super-secret-key-that-is-at-least-32-bytes-long!");
        JwtTokenService verifier = Create(key: "different-key-that-is-at-least-32-bytes-long-x!");

        string token = signer.GenerateAccessToken(UserId, "alice", TenantId, SessionId);
        verifier.ValidateAccessToken(token).Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_WrongIssuer_ReturnsNull()
    {
        JwtTokenService signer = Create(issuer: "Issuer1");
        JwtTokenService verifier = Create(issuer: "Issuer2");

        string token = signer.GenerateAccessToken(UserId, "alice", TenantId, SessionId);
        verifier.ValidateAccessToken(token).Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_WrongAudience_ReturnsNull()
    {
        JwtTokenService signer = Create(audience: "Audience1");
        JwtTokenService verifier = Create(audience: "Audience2");

        string token = signer.GenerateAccessToken(UserId, "alice", TenantId, SessionId);
        verifier.ValidateAccessToken(token).Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_ExpiredToken_ReturnsNull()
    {
        // Mint the token with a clock set well in the past so its `exp` is already behind real-time
        // (and behind the validator's 1-minute clock skew). The handler validates lifetime against the
        // real wall clock, so an already-expired token must be rejected.
        FakeTimeProvider pastClock = new(DateTimeOffset.UtcNow.AddHours(-2));
        JwtTokenService svc = Create(expirationMinutes: "5", timeProvider: pastClock);

        string expiredToken = svc.GenerateAccessToken(UserId, "alice", TenantId, SessionId);

        svc.ValidateAccessToken(expiredToken).Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_EmptyOrGarbage_ReturnsNull()
    {
        JwtTokenService svc = Create();
        svc.ValidateAccessToken(string.Empty).Should().BeNull();
        svc.ValidateAccessToken("not.a.valid.token").Should().BeNull();
    }
}
