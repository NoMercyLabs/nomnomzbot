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
using Microsoft.AspNetCore.Http;
using NomNomzBot.Application.Abstractions.Auth;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// ICurrentUserService implementation that reads the current user
/// from HttpContext claims (populated by JWT authentication).
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserId =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? DisplayName =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue("display_name")
        ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.GivenName);

    public string? Username =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public bool IsPlatformPrincipal =>
        _httpContextAccessor.HttpContext?.User?.IsInRole("admin") ?? false;

    public IEnumerable<string> Roles =>
        _httpContextAccessor.HttpContext?.User?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? [];

    public ImpersonationContext? Impersonation
    {
        get
        {
            ClaimsPrincipal? principal = _httpContextAccessor.HttpContext?.User;
            if (principal is null)
                return null;

            string? actClaim = principal.FindFirstValue(JwtTokenService.ActorClaim);
            string? sidClaim = principal.FindFirstValue(JwtTokenService.SessionClaim);
            string? subClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (
                !Guid.TryParse(actClaim, out Guid operatorUserId)
                || !Guid.TryParse(sidClaim, out Guid sessionId)
                || !Guid.TryParse(subClaim, out Guid subjectUserId)
            )
                return null;

            return new(operatorUserId, subjectUserId, sessionId);
        }
    }
}
