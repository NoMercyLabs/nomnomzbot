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
using NomNomzBot.Infrastructure.Platform.Auth;

namespace NomNomzBot.Infrastructure.Tests.Platform.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService Create(
        string key = "super-secret-key-that-is-at-least-32-bytes-long!",
        string issuer = "TestIssuer",
        string audience = "TestAudience",
        string expirationMinutes = "60"
    )
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    { "Jwt:Key", key },
                    { "Jwt:Issuer", issuer },
                    { "Jwt:Audience", audience },
                    { "Jwt:ExpirationMinutes", expirationMinutes },
                }
            )
            .Build();

        return new(config, TimeProvider.System);
    }

    // ─── GenerateToken ────────────────────────────────────────────────────────

    [Fact]
    public void GenerateToken_ValidInputs_ReturnsNonEmptyString()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateToken("uid1", "alice");

        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateToken_HasThreeJwtParts()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateToken("uid1", "alice");

        token.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public void GenerateToken_DifferentCalls_ProduceDifferentTokens()
    {
        JwtTokenService svc = Create();
        string t1 = svc.GenerateToken("uid1", "alice");
        string t2 = svc.GenerateToken("uid1", "alice");

        // Different JTI claims → different tokens
        t1.Should().NotBe(t2);
    }

    // ─── ValidateToken ────────────────────────────────────────────────────────

    [Fact]
    public void ValidateToken_ValidToken_ReturnsPrincipal()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateToken("uid1", "alice");

        ClaimsPrincipal? principal = svc.ValidateToken(token);

        principal.Should().NotBeNull();
    }

    [Fact]
    public void ValidateToken_ValidToken_ContainsNameIdentifierClaim()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateToken("uid1", "alice");

        ClaimsPrincipal? principal = svc.ValidateToken(token);

        principal!.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be("uid1");
    }

    [Fact]
    public void ValidateToken_ValidToken_ContainsNameClaim()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateToken("uid1", "alice");

        ClaimsPrincipal? principal = svc.ValidateToken(token);

        principal!.FindFirstValue(ClaimTypes.Name).Should().Be("alice");
    }

    [Fact]
    public void ValidateToken_WithRoles_ContainsRoleClaims()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateToken("uid1", "alice", ["admin", "moderator"]);

        ClaimsPrincipal? principal = svc.ValidateToken(token);

        List<string> roles = principal!.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        roles.Should().Contain("admin").And.Contain("moderator");
    }

    [Fact]
    public void ValidateToken_InvalidToken_ReturnsNull()
    {
        JwtTokenService svc = Create();
        ClaimsPrincipal? principal = svc.ValidateToken("not.a.valid.token");

        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_TamperedToken_ReturnsNull()
    {
        JwtTokenService svc = Create();
        string token = svc.GenerateToken("uid1", "alice");
        string[] parts = token.Split('.');
        string tampered = parts[0] + "." + parts[1] + ".INVALIDSIGNATURE";

        ClaimsPrincipal? principal = svc.ValidateToken(tampered);

        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WrongKey_ReturnsNull()
    {
        JwtTokenService svc1 = Create(key: "super-secret-key-that-is-at-least-32-bytes-long!");
        JwtTokenService svc2 = Create(key: "different-key-that-is-at-least-32-bytes-long-x!");

        string token = svc1.GenerateToken("uid1", "alice");
        ClaimsPrincipal? principal = svc2.ValidateToken(token);

        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WrongIssuer_ReturnsNull()
    {
        JwtTokenService svc1 = Create(issuer: "Issuer1");
        JwtTokenService svc2 = Create(issuer: "Issuer2");

        string token = svc1.GenerateToken("uid1", "alice");
        ClaimsPrincipal? principal = svc2.ValidateToken(token);

        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WrongAudience_ReturnsNull()
    {
        JwtTokenService svc1 = Create(audience: "Audience1");
        JwtTokenService svc2 = Create(audience: "Audience2");

        string token = svc1.GenerateToken("uid1", "alice");
        ClaimsPrincipal? principal = svc2.ValidateToken(token);

        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_EmptyString_ReturnsNull()
    {
        JwtTokenService svc = Create();
        ClaimsPrincipal? principal = svc.ValidateToken(string.Empty);

        principal.Should().BeNull();
    }
}
