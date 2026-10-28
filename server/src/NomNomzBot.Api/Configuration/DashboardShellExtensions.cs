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

using Microsoft.Extensions.FileProviders;

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
    private static readonly string[] BotOwnedPrefixes = ["api", "hubs", "health"];

    private const string EditorPath = "/editor";

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
    /// Serves the code editor page from <paramref name="editorFolder"/> under <c>/editor</c>, then ends the
    /// request: an unknown address under <c>/editor</c> is a 404, never the dashboard shell. It ends here, not in
    /// a fallback endpoint, because a matched endpoint (even a fallback) makes the static files middleware pass
    /// the request on, so an <c>/editor</c> fallback answered 404 for the editor's own files.
    /// </summary>
    public static IApplicationBuilder UseEditorStaticFiles(
        this IApplicationBuilder app,
        string editorFolder
    )
    {
        if (Directory.Exists(editorFolder))
        {
            app.UseStaticFiles(
                new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(editorFolder),
                    RequestPath = EditorPath,
                    OnPrepareResponse = ctx =>
                    {
                        // The host always requests with ?v=<build version>, so the URL changes on every deploy
                        // and the bytes behind it never do — safe to cache immutably, and CDN-cacheable for
                        // SaaS. A request without the marker (someone opening /editor/ by hand) must still
                        // revalidate.
                        bool versioned = ctx.Context.Request.Query.ContainsKey("v");
                        ctx.Context.Response.Headers.CacheControl = versioned
                            ? "public, max-age=31536000, immutable"
                            : "no-cache, must-revalidate";
                    },
                }
            );
        }

        return app.UseWhen(
            context => context.Request.Path.StartsWithSegments(EditorPath),
            branch =>
                branch.Run(context =>
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                })
        );
    }

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
