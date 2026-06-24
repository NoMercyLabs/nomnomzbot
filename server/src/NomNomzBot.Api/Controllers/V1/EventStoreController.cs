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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Portable export/import of the channel's append-only event journal (event-store §1.1) — the ledger the owner
/// can carry between deployments or back up. The <c>{channelId}</c> route makes
/// <see cref="TenantResolutionMiddleware"/> resolve and access-check the tenant, so a caller can only export/import
/// their own channel's journal; <see cref="ICurrentTenantService.BroadcasterId"/> is that resolved tenant. Both
/// routes floor at <c>Broadcaster</c> via Gate 2: export reads the entire journal, and import mutates it (so it is
/// also danger-tier Critical in the action catalogue).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/event-store/channels/{channelId}")]
[Authorize]
[Tags("EventStore")]
public class EventStoreController : BaseController
{
    private readonly IEventJournalPortabilityService _portability;
    private readonly ILegacyChannelImportService _legacyImport;
    private readonly ICurrentTenantService _tenant;
    private readonly TimeProvider _clock;

    public EventStoreController(
        IEventJournalPortabilityService portability,
        ILegacyChannelImportService legacyImport,
        ICurrentTenantService tenant,
        TimeProvider clock
    )
    {
        _portability = portability;
        _legacyImport = legacyImport;
        _tenant = tenant;
        _clock = clock;
    }

    /// <summary>
    /// Exports the channel's whole journal as a JSONL file download (one event envelope per line). Owner-only.
    /// </summary>
    [RequireAction("eventstore:export")]
    [HttpPost("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(string channelId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No channel resolved for the caller.");

        MemoryStream buffer = new();
        Result<long> result = await _portability.ExportAsync(broadcasterId, buffer, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        buffer.Position = 0;
        string fileName =
            $"event-journal-{channelId}-{_clock.GetUtcNow().UtcDateTime:yyyyMMddHHmmss}.jsonl";
        return File(buffer, "application/x-ndjson", fileName);
    }

    /// <summary>
    /// Imports a JSONL journal export into the channel's stream — idempotent (duplicates by EventId are skipped),
    /// upcast on read, and atomic (all-or-nothing). Owner-only. Returns import/skip/upcast counts.
    /// </summary>
    [RequireAction("eventstore:import")]
    [HttpPost("import")]
    [ProducesResponseType<StatusResponseDto<EventJournalImportSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Import(string channelId, IFormFile file, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No channel resolved for the caller.");

        if (file is null || file.Length == 0)
            return BadRequestResponse("An import file is required.");

        await using Stream upload = file.OpenReadStream();
        Result<EventJournalImportSummary> result = await _portability.ImportAsync(
            broadcasterId,
            upload,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Backfills the channel's history from the legacy NoMercy bot database into the journal — reads the legacy file
    /// READ-ONLY, maps each channel event to its current domain event, and appends idempotently (a re-run imports
    /// nothing). Owner-only. Returns import/skip counts and the journal head before/after.
    /// </summary>
    [RequireAction("eventstore:import:legacy")]
    [HttpPost("import-legacy")]
    [ProducesResponseType<StatusResponseDto<LegacyImportResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportLegacy(string channelId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No channel resolved for the caller.");

        Result<LegacyImportResult> result = await _legacyImport.ImportLegacyAsync(
            broadcasterId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Rebuilds every tenant-scoped read-model projection (channel event log, analytics dailies, viewer profiles, …)
    /// from the channel's journal — reset → replay from position 0. Owner-level. Run after a legacy import (or any
    /// journal backfill) so the new facts surface in the read models. Returns the per-projection applied-event counts.
    /// </summary>
    [RequireAction("eventstore:projection:rebuild")]
    [HttpPost("rebuild-projections")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ProjectionRebuildResult>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> RebuildProjections(string channelId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No channel resolved for the caller.");

        Result<IReadOnlyList<ProjectionRebuildResult>> result =
            await _legacyImport.RebuildProjectionsAsync(broadcasterId, ct);
        return ResultResponse(result);
    }
}
