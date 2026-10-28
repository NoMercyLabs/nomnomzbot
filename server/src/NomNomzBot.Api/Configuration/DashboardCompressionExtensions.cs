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

using Microsoft.AspNetCore.ResponseCompression;

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// Response compression for the dashboard bundle (its scripts, styles, icons and manifest).
/// </summary>
public static class DashboardCompressionExtensions
{
    /// <summary>
    /// Compresses the Vue dashboard bundle (scripts, styles, SVG icons, the web manifest) and its fonts —
    /// nothing else. Scoped to exactly those MIME types (not the default set, which would also compress
    /// application/json) so an authenticated API response is never compressed alongside attacker-influenceable
    /// input — the classic BREACH-attack precondition this deliberately avoids reintroducing for a load-time
    /// win. EnableForHttps=true because the whole app is served over HTTPS in every real deployment.
    /// </summary>
    public static IServiceCollection AddDashboardResponseCompression(
        this IServiceCollection services
    ) =>
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes =
            [
                "text/javascript",
                "application/javascript",
                "text/css",
                "image/svg+xml",
                "application/manifest+json",
                "font/ttf",
                "font/otf",
                "font/sfnt",
                "application/x-font-ttf",
                "application/x-font-opentype",
                "application/vnd.ms-opentype",
            ];
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
}
