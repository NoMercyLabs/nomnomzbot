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

using Microsoft.AspNetCore.Http;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// Reads the <c>X-View-As</c> request header: <c>moderator</c> caps the caller at Moderator, <c>viewer</c> at
/// Everyone. Any other value is ignored, so the header can never name a level above the caller's own.
/// </summary>
public sealed class HttpViewAsScope(IHttpContextAccessor httpContextAccessor) : IViewAsScope
{
    public const string HeaderName = "X-View-As";

    public int? CeilingLevel =>
        ToCeiling(httpContextAccessor.HttpContext?.Request.Headers[HeaderName].ToString());

    public static int? ToCeiling(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "moderator" => PermissionLevel.Moderator.ToLevelValue(),
            "viewer" => PermissionLevel.Everyone.ToLevelValue(),
            _ => null,
        };
}
