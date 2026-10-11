// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.StaticFiles;

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// Serves the compiled Wasm dashboard bundle from the web root: its static files, and the SPA entry document
/// (<c>index.html</c>) for every address no other endpoint answers (<c>/app</c>, <c>/app/</c>, deep links).
/// </summary>
public static class DashboardShellExtensions
{
    /// <summary>
    /// Serves the bundle's files. The explicit <c>.wasm</c> mapping guarantees the MIME type the browser needs to
    /// instantiate the module. Bundle files with no registered MIME type (the <c>.cvr</c> i18n tables) go out as
    /// octet-stream instead of a 404, which is safe because the web root holds only the public dashboard bundle.
    /// Cache-Control comes from <see cref="StaticAssetCachePolicy"/>.
    /// </summary>
    public static IApplicationBuilder UseDashboardStaticFiles(this IApplicationBuilder app)
    {
        FileExtensionContentTypeProvider contentTypes = new()
        {
            Mappings = { [".wasm"] = "application/wasm" },
        };

        return app.UseStaticFiles(
            new StaticFileOptions
            {
                ContentTypeProvider = contentTypes,
                ServeUnknownFileTypes = true,
                DefaultContentType = "application/octet-stream",
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers.CacheControl = StaticAssetCachePolicy.For(
                        ctx.File.Name
                    );
                },
            }
        );
    }

    /// <summary>
    /// The SPA fallback: any route not matched by an API / hub / health endpoint or a static file serves
    /// <c>index.html</c>. It is served here, not by the static files middleware, so it needs its own
    /// <c>Cache-Control</c>: <c>no-store</c> makes every load re-fetch the shell, so a plain reload always lands
    /// the latest deploy. Returns 404 when no dashboard is bundled (empty web root).
    /// </summary>
    public static IEndpointConventionBuilder MapDashboardFallback(
        this IEndpointRouteBuilder endpoints
    ) =>
        endpoints.MapFallbackToFile(
            "index.html",
            new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                    ctx.Context.Response.Headers.CacheControl = StaticAssetCachePolicy.EntryPoint,
            }
        );
}
