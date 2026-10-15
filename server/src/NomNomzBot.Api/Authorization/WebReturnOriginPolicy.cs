// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using NomNomzBot.Api.Extensions;

namespace NomNomzBot.Api.Authorization;

/// <summary>
/// The trust boundary for the served-web RETURN ORIGIN: the dashboard origin a web sign-in goes back to when it
/// is not the public base URL (a second dashboard, such as a dev site that proxies <c>/api</c> to the bot).
/// The origin the browser used is read from <c>X-Forwarded-Host</c>/<c>X-Forwarded-Proto</c> (first value) or
/// the request host, and every one of those can be forged by the caller. So an origin is kept only when it is
/// listed by the operator: <c>Cors:Origins</c> (exact scheme + host + port), or a loopback origin in
/// Development. Nothing derived from the request itself makes an origin trusted.
/// </summary>
public static class WebReturnOriginPolicy
{
    private static readonly string[] DefaultCorsOrigins =
    [
        "http://localhost:3000",
        "http://localhost:5090",
        "http://localhost:8081",
        "https://bot-dev.nomercy.tv",
        "https://vue.nomnomz.bot",
    ];

    /// <summary>The configured <c>Cors:Origins</c>, or the built-in defaults when the key is absent.</summary>
    public static string[] CorsOrigins(IConfiguration configuration) =>
        configuration.GetSection("Cors:Origins").Get<string[]>() ?? DefaultCorsOrigins;

    /// <summary>
    /// The origin a web flow should return to, when it differs from the public base URL and is listed; null
    /// otherwise (the flow returns to the public base URL, exactly as before).
    /// </summary>
    public static string? Capture(
        HttpRequest request,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        string? origin = BrowserOrigin(request);
        return IsCrossOriginTarget(origin, request, configuration, environment) ? origin : null;
    }

    /// <summary>True when <paramref name="origin"/> is a cross-origin return target that is still listed.</summary>
    public static bool IsCrossOriginTarget(
        [NotNullWhen(true)] string? origin,
        HttpRequest request,
        IConfiguration configuration,
        IHostEnvironment environment
    ) =>
        origin is not null
        && !IsBaseOrigin(origin, request, configuration)
        && IsListed(origin, configuration, environment);

    /// <summary>True when the operator lists <paramref name="origin"/>: <c>Cors:Origins</c>, or loopback in Development.</summary>
    public static bool IsListed(
        string origin,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        if (environment.IsDevelopment() && IsLoopback(origin))
            return true;

        return CorsOrigins(configuration)
            .Any(allowed =>
                string.Equals(allowed.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static bool IsBaseOrigin(
        string origin,
        HttpRequest request,
        IConfiguration configuration
    ) =>
        string.Equals(
            request.ResolvePublicOrigin(configuration),
            origin,
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>The <c>scheme://host[:port]</c> the browser used, or null when it does not parse as a bare http(s) origin.</summary>
    private static string? BrowserOrigin(HttpRequest request)
    {
        string forwardedHost = request.Headers["X-Forwarded-Host"].ToString();
        string forwardedProto = request.Headers["X-Forwarded-Proto"].ToString();
        string host = !string.IsNullOrWhiteSpace(forwardedHost)
            ? forwardedHost.Split(',')[0].Trim()
            : request.Host.Value ?? string.Empty;
        string scheme = !string.IsNullOrWhiteSpace(forwardedProto)
            ? forwardedProto.Split(',')[0].Trim()
            : request.Scheme;

        if (
            !Uri.TryCreate($"{scheme}://{host}", UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.UserInfo.Length > 0
        )
            return null;

        return $"{uri.Scheme}://{uri.Authority}";
    }

    private static bool IsLoopback(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && uri.IsLoopback;
}
