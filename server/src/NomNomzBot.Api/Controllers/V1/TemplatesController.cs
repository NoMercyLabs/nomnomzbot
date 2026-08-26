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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Serves the machine-readable template helper registry (S042) — the same registry
/// <c>TemplateHelperValidator</c> uses at save time — so the dashboard's template editors can offer an
/// autocomplete/insert list instead of streamers guessing at placeholder names.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/templates")]
[Authorize]
[Tags("Templates")]
public sealed class TemplatesController : BaseController
{
    /// <summary>
    /// GET /api/v1/templates/helpers?context=command|eventResponse|timer — the full valid helper set for
    /// that context. An unknown/missing context fails honestly (400) rather than falling back to "all".
    /// </summary>
    [HttpGet("helpers")]
    // Gate 1 is pure entry, so an ungated endpoint is reachable by ANY authenticated user. The helper
    // catalogue is only useful to someone authoring templates on this channel, and every authoring
    // surface it feeds (commands, event responses, timers) already sits behind "commands:read" — so it
    // shares that key rather than inventing a parallel one.
    [RequireAction("commands:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<TemplateHelperDto>>>(
        StatusCodes.Status200OK
    )]
    public IActionResult GetHelpers([FromQuery] string context)
    {
        if (!Enum.TryParse(context, ignoreCase: true, out TemplateHelperContext parsed))
            return BadRequestResponse(
                $"Unknown template context '{context}'. Valid values: "
                    + string.Join(", ", Enum.GetNames<TemplateHelperContext>())
                    + "."
            );

        IReadOnlyList<TemplateHelperDto> helpers =
        [
            .. TemplateHelperRegistry.ForContext(parsed).Select(TemplateHelperDto.FromEntry),
        ];

        return Ok(new StatusResponseDto<IReadOnlyList<TemplateHelperDto>> { Data = helpers });
    }
}
