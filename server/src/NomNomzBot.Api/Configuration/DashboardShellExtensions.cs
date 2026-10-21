// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// Serves the compiled Vue dashboard from the web root: its static files, and the SPA entry document
/// (<c>index.html</c>) for every address no other endpoint answers (<c>/app</c>, <c>/setup</c>, deep links).
/// </summary>
public static class DashboardShellExtensions
{
    /// <summary>
    /// The bot's own address spaces. An unknown address under one of them is a missing endpoint, so it answers
    /// 404 and never the dashboard shell: a client that calls a wrong API route must not receive HTML.
    /// </summary>
    private static readonly string[] BotOwnedPrefixes = ["api", "hubs", "health", "editor"];

    /// <summary>
    /// Serves the bundle's files. A file with no registered MIME type is a 404, not a download: the web root
    /// holds only the public dashboard bundle and every type it contains is known.
    /// Cache-Control comes from <see cref="StaticAssetCachePolicy"/>.
    /// </summary>
    public static IApplicationBuilder UseDashboardStaticFiles(this IApplicationBuilder app) =>
        app.UseStaticFiles(
            new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers.CacheControl = StaticAssetCachePolicy.For(
                        ctx.Context.Request.Path.Value ?? string.Empty
                    );
                },
            }
        );

    /// <summary>
    /// The SPA fallback: any route not matched by an API / hub / health endpoint or a static file serves
    /// <c>index.html</c>. It is served here, not by the static files middleware, so it needs its own
    /// <c>Cache-Control</c>: <c>no-store</c> makes every load re-fetch the shell, so a plain reload always lands
    /// the latest deploy. A path with a file extension (a missing <c>/assets</c> file) never matches, and an
    /// unknown address under <see cref="BotOwnedPrefixes"/> answers 404. Returns 404 when no dashboard is
    /// bundled (empty web root).
    /// </summary>
    public static IEndpointConventionBuilder MapDashboardFallback(
        this IEndpointRouteBuilder endpoints
    )
    {
        foreach (string prefix in BotOwnedPrefixes)
        {
            endpoints
                .MapFallback($"/{prefix}/{{**rest}}", () => Results.NotFound())
                .ExcludeFromDescription();
            endpoints.MapFallback($"/{prefix}", () => Results.NotFound()).ExcludeFromDescription();
        }

        return endpoints.MapFallbackToFile(
            "index.html",
            new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                    ctx.Context.Response.Headers.CacheControl = StaticAssetCachePolicy.EntryPoint,
            }
        );
    }
}
