// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NomNomzBot.Application.Abstractions.Auth;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// Mints and validates the platform access JWT (identity-auth §3.2, §9). HS256 is the default on the
/// single-user self-host path; setting <c>Jwt:Algorithm</c> to <c>RS256</c>/<c>ES256</c> selects asymmetric
/// signing for the federation/SSO path — same interface, the impl picks the key. <c>sub</c> = internal user
/// Guid; <c>tenant</c> = resolved channel; <c>sid</c> = session id. Refresh tokens are opaque random values
/// minted by <see cref="GenerateRefreshTokenValue"/>; the caller hashes and persists them.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    /// <summary>The resolved tenant (channel) the access token is scoped to.</summary>
    public const string TenantClaim = "tenant";

    /// <summary>The auth-session id the access token belongs to.</summary>
    public const string SessionClaim = "sid";

    /// <summary>
    /// The operator ACTING AS the subject on an impersonation token (RFC 8693 <c>act</c>). Purely
    /// informational — NO authorization path may read it, so an impersonation token grants exactly the
    /// impersonated user's access, never the operator's.
    /// </summary>
    public const string ActorClaim = "act";

    /// <summary>The acting operator's display name, alongside <see cref="ActorClaim"/>. Non-authoritative.</summary>
    public const string ActorNameClaim = "act_name";

    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _expiration;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;

    public JwtTokenService(IConfiguration configuration, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        IConfigurationSection jwtSection = configuration.GetSection("Jwt");
        _issuer = jwtSection["Issuer"] ?? "nomnomzbot";
        _audience = jwtSection["Audience"] ?? "nomnomzbot";
        _expiration = TimeSpan.FromMinutes(
            double.Parse(jwtSection["ExpiryMinutes"] ?? jwtSection["ExpirationMinutes"] ?? "60")
        );

        string algorithm = jwtSection["Algorithm"]?.ToUpperInvariant() ?? "HS256";
        (_signingCredentials, SecurityKey validationKey) = algorithm switch
        {
            "RS256" or "ES256" => BuildAsymmetric(jwtSection, algorithm),
            _ => BuildSymmetric(jwtSection),
        };

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = validationKey,
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    }

    public string GenerateAccessToken(
        Guid userId,
        string username,
        Guid? broadcasterId,
        Guid sessionId,
        IEnumerable<string>? roles = null,
        string? idp = null,
        string? actorUserId = null,
        string? actorUsername = null
    )
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new(SessionClaim, sessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(
                JwtRegisteredClaimNames.Iat,
                _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64
            ),
        ];

        if (broadcasterId is { } tenant)
            claims.Add(new(TenantClaim, tenant.ToString()));

        if (roles is not null)
            foreach (string role in roles)
                claims.Add(new(ClaimTypes.Role, role));

        // The login provider this session authenticated with (platform-identity §3.3).
        if (!string.IsNullOrEmpty(idp))
            claims.Add(new("idp", idp));

        // Act-as (impersonation): the operator behind the token. Non-authoritative — recorded for audit
        // and UI banners only. The roles/sub above stay the impersonated user's, never the operator's.
        if (!string.IsNullOrEmpty(actorUserId))
        {
            claims.Add(new(ActorClaim, actorUserId));
            if (!string.IsNullOrEmpty(actorUsername))
                claims.Add(new(ActorNameClaim, actorUsername));
        }

        JwtSecurityToken token = new(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: _timeProvider.GetUtcNow().UtcDateTime.Add(_expiration),
            signingCredentials: _signingCredentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshTokenValue() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, _validationParameters, out _);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (SigningCredentials, SecurityKey) BuildSymmetric(IConfigurationSection jwt)
    {
        byte[] key = Encoding.UTF8.GetBytes(
            jwt["Secret"]
                ?? jwt["Key"]
                ?? throw new InvalidOperationException("JWT Secret is not configured.")
        );
        SymmetricSecurityKey securityKey = new(key);
        return (new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256), securityKey);
    }

    private static (SigningCredentials, SecurityKey) BuildAsymmetric(
        IConfigurationSection jwt,
        string algorithm
    )
    {
        string privatePem =
            jwt["PrivateKeyPem"]
            ?? throw new InvalidOperationException(
                "JWT asymmetric signing requires Jwt:PrivateKeyPem."
            );

        if (algorithm == "ES256")
        {
            ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privatePem);
            ECDsaSecurityKey key = new(ecdsa);
            return (new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256), key);
        }

        RSA rsa = RSA.Create();
        rsa.ImportFromPem(privatePem);
        RsaSecurityKey rsaKey = new(rsa);
        return (new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256), rsaKey);
    }
}
