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

using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// Serves the overlay theme (<c>/overlay/theme.css</c>) and its typeface. The theme holds the tokens and parts every
/// default widget is drawn from; the overlay host links it and the overlay SDK picks the look with the widget's
/// <c>theme</c> setting. Served anonymously: it carries no secrets.
/// </summary>
[ApiController]
[Route("overlay")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(NomNomzBot.Api.RateLimiting.RateLimitPolicyNames.Anonymous)]
public sealed class OverlayThemeController : ControllerBase
{
    private const string ResourcePrefix = "NomNomzBot.Infrastructure.Widgets.Theme.";

    private static readonly byte[] Stylesheet = Load("overlay-theme.css");
    private static readonly byte[] FontLatin = Load("font-latin.woff2");
    private static readonly byte[] FontLatinExt = Load("font-latin-ext.woff2");

    /// <summary>A hash of the stylesheet, so the host page's link changes exactly when the theme does.</summary>
    public static string Version { get; } =
        Convert.ToHexString(SHA256.HashData(Stylesheet)).ToLowerInvariant()[..12];

    private static byte[] Load(string name)
    {
        Assembly assembly = typeof(Infrastructure.DependencyInjection).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourcePrefix + name);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded overlay theme file '{name}' was not found in {assembly.GetName().Name}."
            );
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The theme stylesheet. The host page links it with its hash, so an hour of cache is safe.</summary>
    [HttpGet("theme.css")]
    public IActionResult GetStylesheet()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return File(Stylesheet, "text/css; charset=utf-8");
    }

    /// <summary>The theme typeface, one file per script range. It only changes with a bot upgrade.</summary>
    [HttpGet("theme/font-{range}.woff2")]
    public IActionResult GetFont(string range)
    {
        byte[]? font = range switch
        {
            "latin" => FontLatin,
            "latin-ext" => FontLatinExt,
            _ => null,
        };
        if (font is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(font, "font/woff2");
    }
}
