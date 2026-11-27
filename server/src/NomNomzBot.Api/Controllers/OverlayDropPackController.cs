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

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// Serves the drop game's theme packs beside the overlay theme: <c>/overlay/drop-packs/{packId}/{file}</c> gives a
/// pack's <c>pack.json</c> or a file it names. An unknown pack id is served as the default pack, so a widget whose
/// pack was removed still draws. Served anonymously: a pack is art and carries no secrets.
/// </summary>
[ApiController]
[Route("overlay/drop-packs")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(NomNomzBot.Api.RateLimiting.RateLimitPolicyNames.Anonymous)]
public sealed class OverlayDropPackController : ControllerBase
{
    /// <summary>One file of the pack, or 404 when the pack carries no file of that name.</summary>
    [HttpGet("{packId}/{file}")]
    public IActionResult Get(string packId, string file)
    {
        byte[]? bytes = FirstPartyDropPacks.ReadFile(packId, file);
        if (bytes is null)
            return NotFound();

        // A pack only changes with a bot upgrade; an hour keeps a swap to a new version quick.
        Response.Headers.CacheControl = "public, max-age=3600";
        return File(bytes, ContentTypeOf(file));
    }

    private static string ContentTypeOf(string file) =>
        Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            _ => "application/octet-stream",
        };
}
