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
/// come from <c>ReadAloud:Origins</c>; without that setting the official docs site is allowed.
/// </summary>
public static class ReadAloudCors
{
    public const string PolicyName = "PublicReadAloud";

    private static readonly string[] DefaultOrigins = ["https://docs.nomnomz.bot"];

    public static void Configure(CorsOptions options, IConfiguration configuration) =>
        options.AddPolicy(
            PolicyName,
            policy =>
                policy
                    .WithOrigins(
                        configuration.GetSection("ReadAloud:Origins").Get<string[]>()
                            ?? DefaultOrigins
                    )
                    .WithMethods("GET", "POST")
                    .AllowAnyHeader()
        );
}
