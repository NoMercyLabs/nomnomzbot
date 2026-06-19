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
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NomNomzBot.Application.Abstractions.Auth;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// Generates and validates JWT tokens for API authentication.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly byte[] _key;
    private readonly TimeSpan _expiration;
    private readonly TimeSpan _refreshExpiration;

    public JwtTokenService(IConfiguration configuration)
    {
        IConfigurationSection jwtSection = configuration.GetSection("Jwt");
        _issuer = jwtSection["Issuer"] ?? "nomercybot";
        _audience = jwtSection["Audience"] ?? "nomercybot";
        _key = Encoding.UTF8.GetBytes(
            jwtSection["Secret"]
                ?? jwtSection["Key"]
                ?? throw new InvalidOperationException("JWT Secret is not configured.")
        );
        _expiration = TimeSpan.FromMinutes(
            double.Parse(jwtSection["ExpiryMinutes"] ?? jwtSection["ExpirationMinutes"] ?? "60")
        );
        _refreshExpiration = TimeSpan.FromDays(
            double.Parse(jwtSection["RefreshExpiryDays"] ?? "7")
        );
    }

    public string GenerateToken(string userId, string username, IEnumerable<string>? roles = null)
    {
        List<Claim> claims = new()
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(
                JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64
            ),
        };

        if (roles is not null)
        {
            foreach (string role in roles)
            {
                claims.Add(new(ClaimTypes.Role, role));
            }
        }

        SymmetricSecurityKey securityKey = new(_key);
        SigningCredentials credentials = new(securityKey, SecurityAlgorithms.HmacSha256);

        JwtSecurityToken token = new(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(_expiration),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken(string userId, string username)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(
                JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64
            ),
            new(ClaimTypes.Role, "refresh"),
        ];

        SymmetricSecurityKey securityKey = new(_key);
        SigningCredentials credentials = new(securityKey, SecurityAlgorithms.HmacSha256);

        JwtSecurityToken token = new(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(_refreshExpiration),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        JwtSecurityTokenHandler tokenHandler = new();
        TokenValidationParameters validationParameters = new()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(_key),
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        try
        {
            return tokenHandler.ValidateToken(token, validationParameters, out _);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
