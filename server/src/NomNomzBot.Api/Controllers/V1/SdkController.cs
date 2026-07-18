// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.DevPlatform.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The developer-platform SDK surface (dev-platform.md §1.3, §8) — serves the reflection-generated TypeScript
/// types and the event catalog straight from the running server, so an editor's types always match the live
/// event set. Both endpoints are Gate-2 gated on <c>sdk:read</c> (any authenticated caller on their own
/// channel); the <c>?context=</c> selector chooses the visibility tier set (<c>widget</c> = Public only,
/// <c>script</c> = up to Broadcaster).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sdk")]
[Authorize]
[Tags("Developer SDK")]
public class SdkController : BaseController
{
    private readonly ISdkTypeEmitter _emitter;

    public SdkController(ISdkTypeEmitter emitter) => _emitter = emitter;

    /// <summary>
    /// The generated <c>nnz.d.ts</c> for the requested context, as <c>text/plain</c> — the <c>NnzEventMap</c>,
    /// every payload interface, and the typed <c>nnz.on&lt;K&gt;</c> declaration. <c>400</c> on an unknown context.
    /// </summary>
    [HttpGet("types.d.ts")]
    [RequireAction("sdk:read")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetTypes([FromQuery] string? context)
    {
        if (!TryParseContext(context, out SdkContext parsed))
            return BadRequestResponse("Unknown context — use 'widget' or 'script'.");

        return Content(_emitter.EmitTypeScript(parsed), "text/plain");
    }

    /// <summary>
    /// The event catalog for the requested context — one item per visible event: stable wire name, visibility
    /// tier, and the payload JSON Schema. <c>400</c> on an unknown context.
    /// </summary>
    [HttpGet("event-catalog")]
    [RequireAction("sdk:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<EventCatalogItemDto>>>(
        StatusCodes.Status200OK
    )]
    public IActionResult GetEventCatalog([FromQuery] string? context)
    {
        if (!TryParseContext(context, out SdkContext parsed))
            return BadRequestResponse("Unknown context — use 'widget' or 'script'.");

        return Ok(
            new StatusResponseDto<IReadOnlyList<EventCatalogItemDto>>
            {
                Data = _emitter.EmitEventCatalog(parsed),
            }
        );
    }

    // Missing context defaults to the fuller 'script' set; a present-but-unrecognised value is a 400.
    private static bool TryParseContext(string? context, out SdkContext parsed)
    {
        if (
            string.IsNullOrWhiteSpace(context)
            || context.Equals("script", StringComparison.OrdinalIgnoreCase)
        )
        {
            parsed = SdkContext.Script;
            return true;
        }
        if (context.Equals("widget", StringComparison.OrdinalIgnoreCase))
        {
            parsed = SdkContext.Widget;
            return true;
        }
        parsed = SdkContext.Script;
        return false;
    }
}
