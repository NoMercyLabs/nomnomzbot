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

namespace NomNomzBot.Application.Abstractions.Auth;

/// <summary>
/// Generates and validates JWT tokens for API authentication.
/// </summary>
public interface IJwtTokenService
{
    string GenerateToken(string userId, string username, IEnumerable<string>? roles = null);
    string GenerateRefreshToken(string userId, string username);
    ClaimsPrincipal? ValidateToken(string token);
}
