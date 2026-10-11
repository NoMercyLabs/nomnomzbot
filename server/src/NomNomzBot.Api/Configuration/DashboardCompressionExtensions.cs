// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.ResponseCompression;

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// Response compression for the dashboard bundle (the Wasm module, its script, and its bundled files).
/// </summary>
public static class DashboardCompressionExtensions
{
    /// <summary>
    /// Compresses the Wasm dashboard bundle (tens of MB uncompressed) and its JS/CSS/octet-stream siblings —
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
                "application/wasm",
                "text/javascript",
                "application/javascript",
                "text/css",
                "application/octet-stream",
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
