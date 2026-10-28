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
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// The public pages' Read Aloud (the Privacy Policy and Terms of Service): one short piece of page text in, the
/// bot's own neural voice out, as an MP3. Signed-out visitors use it, so it is rate limited per IP like every
/// anonymous surface, takes at most <see cref="ReadAloudService"/> limits of text, and picks the voice itself.
/// Left out of the OpenAPI snapshot: the page calls it directly. The GET form carries a long public cache header,
/// so the browser and Cloudflare keep each sentence; the POST form serves the dashboard. Pages on another origin
/// are allowed by <see cref="ReadAloudCors"/>, without credentials.
/// </summary>
[ApiController]
[Route("api/v1/public/read-aloud")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(RateLimiting.RateLimitPolicyNames.Anonymous)]
[EnableCors(ReadAloudCors.PolicyName)]
public sealed class ReadAloudController(IReadAloudService readAloud) : ControllerBase
{
    private const string PublicCacheControl = "public, max-age=604800, s-maxage=2592000";

    /// <summary>The request body: the text to speak and the page language.</summary>
    public sealed record ReadAloudRequest(string Text, string Locale);

    [HttpPost]
    public async Task<IActionResult> Speak(
        [FromBody] ReadAloudRequest request,
        CancellationToken ct
    ) => await SynthesizeAsync(request.Text, request.Locale, ct);

    [HttpGet]
    public async Task<IActionResult> SpeakCached(
        [FromQuery] string text,
        [FromQuery] string locale,
        CancellationToken ct
    )
    {
        IActionResult answer = await SynthesizeAsync(text, locale, ct);
        if (answer is FileContentResult)
            Response.Headers.CacheControl = PublicCacheControl;

        return answer;
    }

    private async Task<IActionResult> SynthesizeAsync(
        string text,
        string locale,
        CancellationToken ct
    )
    {
        Result<TtsResult> result = await readAloud.SynthesizeAsync(text, locale, ct);
        if (result.IsSuccess)
            return File(result.Value.AudioData, "audio/mpeg");

        return StatusCode(
            result.ErrorCode == "VALIDATION_FAILED"
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status503ServiceUnavailable
        );
    }
}
