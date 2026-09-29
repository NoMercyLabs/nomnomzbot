// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Extensions;

namespace NomNomzBot.Api.Authentication;

/// <summary>
/// The served-web custody of an act-as session: the act-as access token in an HttpOnly cookie, so a page
/// reload comes back as the impersonated user instead of the operator. It rides only to the auth endpoints
/// (the same path as the refresh cookie), where <c>/auth/refresh</c> re-mints it while its support session is
/// open. JS can never read it. <c>SameSite=Strict</c>: unlike the refresh cookie it never has to survive a
/// top-level navigation back from an OAuth provider. No expiry of its own — the support session bounds it
/// server-side, and every way that session ends makes the cookie worthless.
/// </summary>
public static class ActAsCookie
{
    public const string Name = "nnz_act_as";

    private const string CookiePath = "/api/v1/auth";

    /// <summary>The act-as token the request carries, or null when it carries none.</summary>
    public static string? Read(HttpRequest request)
    {
        string? value = request.Cookies[Name];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static void Set(HttpContext http, IConfiguration configuration, string actAsToken) =>
        http.Response.Cookies.Append(Name, actAsToken, Options(http.Request, configuration));

    public static void Clear(HttpContext http, IConfiguration configuration) =>
        http.Response.Cookies.Delete(Name, Options(http.Request, configuration));

    // A delete must carry the same Path the cookie was set with, or the browser keeps the original.
    private static CookieOptions Options(HttpRequest request, IConfiguration configuration) =>
        new()
        {
            HttpOnly = true,
            Secure = request.IsPublicOriginHttps(configuration),
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
        };
}
