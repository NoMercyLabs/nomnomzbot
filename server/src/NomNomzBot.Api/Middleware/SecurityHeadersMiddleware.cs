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
/// tag), the OBS bridge page (<c>ObsBridgeHostController</c>, same per-response nonce pattern — its one
/// inline &lt;script&gt; was being flatly blocked by this middleware's stricter header before it got its own
/// policy), and Scalar's interactive docs (its own inline bootstrap script would be blocked by this policy's
/// <c>script-src</c>). <c>/editor</c> gets its own near-identical policy — see
/// <c>EditorContentSecurityPolicy</c>. Every remaining path is either the Compose/Wasm dashboard shell (<c>index.html</c> /
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
    // script-src's host list already covers — no 'unsafe-inline' needed for those).
    // 'unsafe-eval' is a leftover of the era when the code editor lived inside the shell and routed its CDN
    // imports through `new Function('u','return import(u)')`. The editor is now its own page under /editor with
    // its own policy below, and no `new Function` remains in the client — but the Kotlin/Wasm runtime's own
    // need for it has not been re-verified, so removing it is its own change, not a side effect of the editor
    // move. The overlay/widget host keeps its strict per-widget nonce policy untouched.
    private const string DashboardContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self' 'unsafe-eval' 'wasm-unsafe-eval' https://esm.sh https://cdn.jsdelivr.net; "
        // The CDN belongs on style-src as well as script-src: Monaco's AMD css plugin loads
        // editor.main.css as a real <link>, and `vs/editor/editor.main` does not resolve until that
        // stylesheet has loaded. Omitting the host here does not merely leave the editor unstyled — the
        // module graph never completes, the `monaco` global never appears, and the editor silently falls
        // back to a plain textarea.
        + "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; "
        + "img-src 'self' https: data: blob:; "
        + "media-src 'self' https: blob: data:; "
        + "font-src 'self' https: data:; "
        + "connect-src 'self' https: wss: ws:; "
        + "worker-src 'self' blob:; "
        + "base-uri 'self'; object-src 'none'; frame-ancestors 'self'";

    // Code-editor page CSP (/editor). Identical to the dashboard's except for 'unsafe-inline' on script-src,
    // which the live preview cannot work without: the preview is a client-built document handed to a
    // `srcdoc` iframe, and a srcdoc document INHERITS the creator's policy — so its import map, its SDK stub
    // and the esbuild bundle itself are all inline scripts judged against this header. Under the dashboard
    // policy all three are blocked and the preview pane renders an empty frame with no error in the UI.
    // A nonce cannot help (these assets are static files, so there is no per-response nonce to mint) and
    // neither can a hash (the bundle changes on every keystroke). The relaxation is contained: the only
    // inline script it ever admits runs inside `sandbox="allow-scripts"` — an opaque origin with no
    // same-origin access, no storage and no cookies — and the editor page itself renders every string it
    // shows via textContent, so it has no inline-injection surface of its own.
    private const string EditorContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self' 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' https://esm.sh https://cdn.jsdelivr.net; "
        + "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; "
        + "img-src 'self' https: data: blob:; "
        + "media-src 'self' https: blob: data:; "
        + "font-src 'self' https: data:; "
        + "connect-src 'self' https: wss: ws:; "
        + "worker-src 'self' blob:; "
        + "base-uri 'self'; object-src 'none'; frame-ancestors 'self'";

    private const string EditorPathPrefix = "/editor";

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
    // of it: the overlay/widget host and the OBS bridge page (both emit a strict per-response nonce policy via
    // <meta http-equiv> — a second, stricter header here would intersect with it and still block their one
    // legitimately-inline, nonce-carrying <script>) and Scalar's docs UI.
    private static readonly string[] SelfManagedCspPathPrefixes =
    [
        "/overlay",
        "/obs-bridge",
        "/scalar",
    ];

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

        string? policy = ResolveContentSecurityPolicy(context.Request.Path);
        if (policy is not null)
            context.Response.Headers["Content-Security-Policy"] = policy;

        return _next(context);
    }

    /// <summary>The policy this request's path should carry, or <c>null</c> when it must carry none.</summary>
    private static string? ResolveContentSecurityPolicy(PathString path)
    {
        string value = path.Value ?? string.Empty;
        foreach (string prefix in NonHtmlPathPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        foreach (string prefix in SelfManagedCspPathPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return value.StartsWith(EditorPathPrefix, StringComparison.OrdinalIgnoreCase)
            ? EditorContentSecurityPolicy
            : DashboardContentSecurityPolicy;
    }
}
