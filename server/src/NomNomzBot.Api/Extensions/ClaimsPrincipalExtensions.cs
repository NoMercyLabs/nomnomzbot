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

namespace NomNomzBot.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier);

    public static string? GetBroadcasterId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("broadcaster_id");

    public static string? GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("display_name") ?? principal.FindFirstValue(ClaimTypes.Name);

    public static string GetRequiredUserId(this ClaimsPrincipal principal) =>
        principal.GetUserId() ?? throw new UnauthorizedAccessException("User ID claim is missing.");

    public static string GetRequiredBroadcasterId(this ClaimsPrincipal principal) =>
        principal.GetBroadcasterId()
        ?? throw new UnauthorizedAccessException("Broadcaster ID claim is missing.");
}
