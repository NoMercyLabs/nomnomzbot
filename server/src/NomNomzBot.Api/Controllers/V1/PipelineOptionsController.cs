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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The option-supply side of the pipeline builder's resource pickers (S-RICH-PICKERS): for a given
/// <see cref="PipelineActionFieldKind"/> resource-picker kind, returns the recognisable list of items an
/// operator picks from, instead of the raw id box the field schema alone leaves them facing. Tenant-scoped and
/// gated like every other pipeline-builder read.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/pipelines/options")]
[Authorize]
[Tags("Pipelines")]
public sealed class PipelineOptionsController : BaseController
{
    private readonly IPipelineOptionRegistry _registry;
    private readonly ICurrentTenantService _currentTenant;

    public PipelineOptionsController(
        IPipelineOptionRegistry registry,
        ICurrentTenantService currentTenant
    )
    {
        _registry = registry;
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// The option list for one resource-picker kind (<c>reward</c>, <c>widget</c>, <c>voice</c>,
    /// <c>sound_clip</c>, <c>discord_channel</c>, <c>discord_role</c>, <c>twitch_user</c>, <c>asset</c>, <c>script</c>, <c>music_playlist</c>, <c>twitch_reward</c>, <c>obs_audio_input</c>), paged
    /// and optionally search-filtered. With <c>value</c> set, only the option whose stored value equals it
    /// exactly comes back (search is ignored), so the dashboard can label a saved value that is not on the
    /// first page. A source that is not connected or could not be read comes back with
    /// <c>sourceAvailable: false</c> and a <c>unavailableReason</c> — never collapsed into an empty list.
    /// </summary>
    [HttpGet("{kind}")]
    [RequireAction("pipelines:read")]
    [ProducesResponseType<StatusResponseDto<PipelineOptionListResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOptions(
        string kind,
        [FromQuery] PageRequestDto request,
        [FromQuery] string? value,
        [FromQuery] List<string>? context,
        CancellationToken ct
    )
    {
        if (_currentTenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse();

        if (!TryParseKind(kind, out PipelineActionFieldKind parsedKind))
            return BadRequestResponse($"'{kind}' is not a resource-picker field kind.");

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PipelineOptionListResult> result = await _registry.GetOptionsAsync(
            parsedKind,
            broadcasterId,
            new PipelineOptionQuery(request.Search, value, pagination, ParseContext(context)),
            ct
        );

        return ResultResponse(result);
    }

    /// <summary>Reads the <c>context=name=value</c> query entries into a map; an entry without a name is dropped.</summary>
    private static Dictionary<string, string> ParseContext(List<string>? entries)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in entries ?? [])
        {
            int split = entry.IndexOf('=');
            if (split > 0)
                map[entry[..split]] = entry[(split + 1)..];
        }

        return map;
    }

    private static bool TryParseKind(string wireName, out PipelineActionFieldKind kind)
    {
        foreach (PipelineActionFieldKind candidate in Enum.GetValues<PipelineActionFieldKind>())
        {
            if (string.Equals(candidate.ToWireName(), wireName, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }
}
