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

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// Decides which sites may use the public Read Aloud route. A browser always sends an <c>Origin</c> header on a
/// request from another site, so the route answers only a request whose <c>Origin</c> is one of the allowed docs
/// sites (<see cref="ReadAloudCors.AllowedOrigins"/>) or the site named in <c>App:BaseUrl</c>, which serves the
/// bot itself. A request with no <c>Origin</c> is refused: a plain script or <c>curl</c> sends none by default.
/// The bot's own site comes only from that setting, never from the request: a caller sets its own <c>Host</c>
/// and <c>X-Forwarded-Host</c>, so they prove nothing.
/// <para>
/// The header can be forged by a program that is not a browser, so this keeps other websites from using the
/// voice; it is not the only guard. The route is also rate limited per IP, limits its text, runs a few
/// syntheses at a time, and its clips are cached per site on Cloudflare, so a repeated sentence costs nothing.
/// </para>
/// </summary>
public sealed class ReadAloudOriginGate(IConfiguration configuration)
{
    // The bot's own pages (the dashboard) call it from the site named in App:BaseUrl.
    private readonly HashSet<string> allowed = new(
        [
            .. ReadAloudCors.AllowedOrigins(configuration),
            .. new[] { configuration["App:BaseUrl"]?.TrimEnd('/') }.OfType<string>(),
        ],
        StringComparer.OrdinalIgnoreCase
    );

    public bool Allows(HttpRequest request) => allowed.Contains(request.Headers.Origin.ToString());
}
