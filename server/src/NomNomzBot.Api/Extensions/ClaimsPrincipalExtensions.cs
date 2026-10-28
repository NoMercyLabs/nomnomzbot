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

namespace NomNomzBot.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier);

    public static string? GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("display_name") ?? principal.FindFirstValue(ClaimTypes.Name);

    public static string GetRequiredUserId(this ClaimsPrincipal principal) =>
        principal.GetUserId() ?? throw new UnauthorizedAccessException("User ID claim is missing.");
}
