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

using Microsoft.AspNetCore.Cors.Infrastructure;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// The CORS policy of the public Read Aloud route. It lists the sites that may call the route from a browser and
/// never sends credentials, so it widens nothing else: the default policy of the API stays as it is. The sites
/// come from <c>ReadAloud:Origins</c>; without that setting the official docs site and its dev copy are allowed.
/// </summary>
public static class ReadAloudCors
{
    public const string PolicyName = "PublicReadAloud";

    private static readonly string[] DefaultOrigins =
    [
        "https://docs.nomnomz.bot",
        "https://docs-dev.nomnomz.bot",
    ];

    /// <summary>The allowed sites: the <c>ReadAloud:Origins</c> setting, or the official docs sites.</summary>
    public static string[] AllowedOrigins(IConfiguration configuration) =>
        configuration.GetSection("ReadAloud:Origins").Get<string[]>() ?? DefaultOrigins;

    // The header is "*" for every caller, never the caller's own origin: Cloudflare keeps one copy of a clip for
    // all callers (a cache key with the Origin header needs an Enterprise plan), so a header that named one
    // site would be served to the others. Which sites may call is decided by <see cref="ReadAloudOriginGate"/>
    // at the bot and by a rule on Origin at Cloudflare; the route sends no credentials, so "*" widens nothing.
    public static void Configure(CorsOptions options) =>
        options.AddPolicy(
            PolicyName,
            policy => policy.AllowAnyOrigin().WithMethods("GET", "POST").AllowAnyHeader()
        );
}
