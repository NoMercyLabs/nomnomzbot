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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Import.Dtos;
using NomNomzBot.Application.Import.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// One-shot configuration import from another bot platform into a channel. Gate 1 is <c>[Authorize]</c> + tenant
/// resolution from the <c>{channelId}</c> route; Gate 2 is <c>commands:write</c> — the same write floor a
/// hand-created command sits behind, since an import is a bulk write of the same surfaces. Only the
/// commands / quotes / timers surfaces are imported today; overlays and widgets are a future import.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/import")]
[Authorize]
[Tags("Import")]
public class ImportController : BaseController
{
    private readonly IProviderImportService _import;

    public ImportController(IProviderImportService import)
    {
        _import = import;
    }

    /// <summary>Import commands, quotes, and timers from a StreamElements chatbot export.</summary>
    [RequireAction("commands:write")]
    [HttpPost("streamelements")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [ProducesResponseType<StatusResponseDto<ImportSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportStreamElements(
        string channelId,
        [FromBody] StreamElementsExport export,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse($"Invalid channel ID '{channelId}'.");

        Result<ImportSummary> result = await _import.ImportStreamElementsAsync(
            broadcasterId,
            export,
            ct
        );
        return ResultResponse(result);
    }
}
