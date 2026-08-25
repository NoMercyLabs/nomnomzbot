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

namespace NomNomzBot.Api.Middleware;

/// <summary>
/// Transport + content security headers for the SPA/HTML entry surface (S098d). Two independent policies,
/// both decided from the REQUEST path — not by sniffing the eventual response <c>Content-Type</c> — because
/// the static-file and SPA-fallback middlewares that produce the actual HTML/asset bytes run downstream of
/// this one and, for a byte-streamed static response, start writing (and therefore lock the header
/// collection) before control ever returns here:
/// <list type="bullet">
/// <item><description><c>Strict-Transport-Security</c> on every HTTPS response, everywhere except
/// Development (a local HTTP dev loop must never be told to force HTTPS).</description></item>
/// <item><description>A <c>Content-Security-Policy</c> on every path EXCEPT the JSON/API/hub/health/
/// automation surfaces and the paths that already own their CSP: the overlay/widget host
/// (<c>OverlayHostController</c> emits a strict per-widget nonce policy via a <c>&lt;meta http-equiv&gt;</c>
/// tag) and Scalar's interactive docs (its own inline bootstrap script would be blocked by this policy's
/// <c>script-src</c>). Every remaining path is either the Compose/Wasm dashboard shell (<c>index.html</c> /
/// its static assets) or the SPA fallback, which needs <c>wasm-unsafe-eval</c> for the Kotlin/Wasm runtime
/// plus the CDN hosts the in-app code editor dynamically imports (esm.sh for React/Vue/CodeMirror/
/// TypeScript/esbuild-wasm, cdn.jsdelivr.net for the TypeScript lib files and the emoji sprite sheet). A CSP
/// header on a non-document asset response (e.g. a <c>.wasm</c> chunk) is inert — browsers only enforce it
/// against documents/navigations — so applying it by path rather than by sniffed content-type costs
/// nothing.</description></item>
/// </list>
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    // Compose/Wasm dashboard shell CSP. 'unsafe-inline' on style-src covers Compose's inlined style attributes;
    // there is no inline <script> in the shell itself (the CDN imports are dynamic ES module imports, which
    // script-src's host list already covers — no 'unsafe-inline'/'unsafe-eval' needed for those).
    private const string DashboardContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self' 'wasm-unsafe-eval' https://esm.sh https://cdn.jsdelivr.net; "
        + "style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' https: data: blob:; "
        + "media-src 'self' https: blob: data:; "
        + "font-src 'self' https: data:; "
        + "connect-src 'self' https: wss: ws:; "
        + "worker-src 'self' blob:; "
        + "base-uri 'self'; object-src 'none'; frame-ancestors 'self'";

    private const string HstsHeaderValue = "max-age=31536000; includeSubDomains";

    // Paths that are never the dashboard document: JSON API, SignalR hubs, the raw-WebSocket automation
    // stream, and the health probes. These never receive the dashboard's HTML CSP.
    private static readonly string[] NonHtmlPathPrefixes =
    [
        "/api",
        "/hubs",
        "/automation",
        "/health",
    ];

    // Paths that manage their own Content-Security-Policy and must not receive the dashboard's HTML CSP on top
    // of it: the overlay/widget host (per-widget nonce policy via <meta http-equiv>) and Scalar's docs UI.
    private static readonly string[] SelfManagedCspPathPrefixes = ["/overlay", "/scalar"];

    private readonly RequestDelegate _next;
    private readonly bool _isDevelopment;
    private readonly IConfiguration _configuration;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        IHostEnvironment environment,
        IConfiguration configuration
    )
    {
        _next = next;
        _isDevelopment = environment.IsDevelopment();
        _configuration = configuration;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // The browser's scheme, not the last internal hop's: behind a TLS-terminating proxy Request.IsHttps
        // is false, so HSTS was never sent on any proxied https deployment.
        if (!_isDevelopment && context.Request.IsPublicOriginHttps(_configuration))
            context.Response.Headers["Strict-Transport-Security"] = HstsHeaderValue;

        if (IsEligibleForDashboardCsp(context.Request.Path))
            context.Response.Headers["Content-Security-Policy"] = DashboardContentSecurityPolicy;

        return _next(context);
    }

    private static bool IsEligibleForDashboardCsp(PathString path)
    {
        string value = path.Value ?? string.Empty;
        foreach (string prefix in NonHtmlPathPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (string prefix in SelfManagedCspPathPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
