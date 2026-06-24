// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Extensions;

/// <summary>
/// The single source of truth for the bot's <b>public origin</b> — the <c>scheme://host[:port]</c> a user's
/// browser actually reaches the dashboard on — used to build every OAuth <c>redirect_uri</c> (Twitch, Discord,
/// Spotify, YouTube, bot OAuth) and the "register this redirect URL" copy on the credential card. The two must be
/// computed identically: what the owner registers with the provider has to match, byte for byte, what the bot
/// sends in the authorize/token requests.
/// <para>
/// Resolution precedence (deployment-distribution §6):
/// <list type="number">
///   <item>An explicit, <b>non-loopback</b> <c>App:BaseUrl</c> — a domain or tunnel the operator deliberately
///         fronts the bot with. It owns the public URL and always wins.</item>
///   <item>Otherwise the forwarded request origin — the scheme + host the request arrived on
///         (<c>X-Forwarded-Proto</c>/<c>X-Forwarded-Host</c> from the operator's reverse proxy / Cloudflare
///         tunnel, surfaced by <c>UseForwardedHeaders</c>; or the direct <c>Request.Host</c> for a same-host
///         call). This is what makes a tunnel "just work" — the redirect tracks the domain the dashboard was
///         served from, never the loopback the listener happens to bind.</item>
///   <item>Otherwise a loopback fallback — the configured (loopback) <c>App:BaseUrl</c> or
///         <c>http://localhost:5080</c> — for a headless context with no request host.</item>
/// </list>
/// The loopback default that <c>ListenPortBootstrap</c> writes into <c>App:BaseUrl</c> is intentionally treated as
/// "no explicit URL" here, so a real forwarded host is never overridden by the auto-set loopback value.
/// </para>
/// </summary>
public static class PublicOriginExtensions
{
    private const string DefaultLoopbackOrigin = "http://localhost:5080";

    /// <summary>
    /// Resolves the public origin (no trailing slash) for this request per the precedence documented on
    /// <see cref="PublicOriginExtensions"/>. Always returns an absolute <c>scheme://host[:port]</c>.
    /// </summary>
    public static string ResolvePublicOrigin(this HttpRequest request, IConfiguration configuration)
    {
        // 1. An explicit, non-loopback App:BaseUrl is the operator's deliberate public URL and wins outright.
        string? configuredBaseUrl = configuration["App:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl) && !IsLoopback(configuredBaseUrl))
            return configuredBaseUrl.TrimEnd('/');

        // 2. The forwarded request origin — the scheme + host the browser actually reached us on. Reading the
        //    X-Forwarded-* headers directly (in addition to Request.Scheme/Host, which UseForwardedHeaders fills
        //    in from a trusted proxy) means a tunnel works even when the proxy is not in the trusted-proxy list:
        //    a forged host can only break the attacker's own OAuth flow (the redirect_uri must still match what
        //    is registered with the provider), never another tenant's.
        string forwardedHost = request.Headers["X-Forwarded-Host"].ToString();
        string forwardedProto = request.Headers["X-Forwarded-Proto"].ToString();

        string? host = !string.IsNullOrWhiteSpace(forwardedHost)
            ? forwardedHost.Split(',')[0].Trim()
            : request.Host.Value;
        string? scheme = !string.IsNullOrWhiteSpace(forwardedProto)
            ? forwardedProto.Split(',')[0].Trim()
            : request.Scheme;

        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(scheme))
            return $"{scheme}://{host}";

        // 3. No request host at all (a headless / background context) — fall back to the configured loopback
        //    base URL, or the self-host default port.
        return (configuredBaseUrl ?? DefaultLoopbackOrigin).TrimEnd('/');
    }

    /// <summary>True if <paramref name="url"/> parses to a loopback host (<c>localhost</c> / <c>127.0.0.1</c> / <c>[::1]</c>).</summary>
    private static bool IsLoopback(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host == "127.0.0.1"
            || uri.Host == "[::1]"
            || uri.Host == "::1"
        );
}
