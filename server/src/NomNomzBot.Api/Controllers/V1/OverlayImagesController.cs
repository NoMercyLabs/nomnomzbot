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

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Overlays.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The overlay image optimizer: a browser source (an <c>img</c> tag sends no header, so this is anonymous) asks for
/// an allow-listed remote image at the width it draws and gets a cached WebP back. Which hosts are allowed lives in
/// <see cref="IOverlayImageOptimizer"/>; the server's own origin is the configured <c>App:BaseUrl</c>, never a
/// forwarded header a caller could set.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/overlay/images")]
[AllowAnonymous]
[Tags("Overlay")]
[EnableRateLimiting(RateLimitPolicyNames.OverlayImages)]
public class OverlayImagesController : BaseController
{
    private const string ImmutableForAYear = "public, max-age=31536000, immutable";

    private readonly IOverlayImageOptimizer _optimizer;
    private readonly IConfiguration _config;

    public OverlayImagesController(IOverlayImageOptimizer optimizer, IConfiguration config)
    {
        _optimizer = optimizer;
        _config = config;
    }

    /// <summary>The source image as WebP at the width bucket for <paramref name="w"/>.</summary>
    [HttpGet]
    // Per status, not [Produces]: a [Produces] filter forces every body to image/webp, so a JSON refusal became 406.
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "image/webp")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(typeof(StatusResponseDto<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(StatusResponseDto<object>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Get(
        [FromQuery] string? src,
        [FromQuery] int w,
        CancellationToken ct
    )
    {
        Result<OptimizedImage> result = await _optimizer.OptimizeAsync(
            src ?? string.Empty,
            w,
            _config["App:BaseUrl"],
            ct
        );
        if (result.IsFailure)
            return result.ErrorCode == IOverlayImageOptimizer.ErrorCodes.FetchFailed
                ? StatusCode(
                    StatusCodes.Status502BadGateway,
                    new StatusResponseDto<object>
                    {
                        Status = "error",
                        Message = result.ErrorMessage,
                        Code = result.ErrorCode,
                    }
                )
                : BadRequestResponse(result.ErrorMessage, result.ErrorCode);

        OptimizedImage image = result.Value;
        Response.Headers.CacheControl = ImmutableForAYear;
        Response.Headers.ETag = image.ETag;
        if (Request.Headers.IfNoneMatch.Contains(image.ETag))
            return StatusCode(StatusCodes.Status304NotModified);

        return PhysicalFile(image.FilePath, "image/webp");
    }
}
