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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Portable export/import of the channel's append-only event journal (event-store §1.1) — the ledger the owner
/// can carry between deployments or back up. The <c>{channelId}</c> route makes
/// <see cref="NomNomzBot.Api.Middleware.TenantResolutionMiddleware"/> resolve and access-check the tenant, so a caller can only export/import
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
    private readonly IEventJournal _journal;
    private readonly IEventJournalPortabilityService _portability;
    private readonly ILegacyChannelImportService _legacyImport;
    private readonly IProjectionRunner _projectionRunner;
    private readonly ICurrentTenantService _tenant;
    private readonly TimeProvider _clock;

    public EventStoreController(
        IEventJournal journal,
        IEventJournalPortabilityService portability,
        ILegacyChannelImportService legacyImport,
        IProjectionRunner projectionRunner,
        ICurrentTenantService tenant,
        TimeProvider clock
    )
    {
        _journal = journal;
        _portability = portability;
        _legacyImport = legacyImport;
        _projectionRunner = projectionRunner;
        _tenant = tenant;
        _clock = clock;
    }

    /// <summary>
    /// Reads one page of the channel's journal, newest first, with the channel's head position. Filters are
    /// optional: <c>eventType</c> and <c>source</c> match exactly, <c>from</c> and <c>to</c> bound the time the
    /// event happened. Rows carry no payload (it can hold sealed personal data). Only the resolved channel's own
    /// stream is read.
    /// </summary>
    [RequireAction("eventstore:journal:read")]
    [HttpGet("journal")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<EventJournalPageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReadJournal(
        string channelId,
        [FromQuery] string? eventType,
        [FromQuery] string? source,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No channel resolved for the caller.");

        Result<PagedList<EventRecord>> page = await _journal.QueryAsync(
            new(broadcasterId, eventType, from, to, null, request.Page, request.Take, source),
            ct
        );
        if (page.IsFailure)
            return ResultResponse(page);

        Result<long> head = await _journal.GetHeadPositionAsync(broadcasterId, ct);
        if (head.IsFailure)
            return ResultResponse(head);

        EventJournalPageDto body = new(
            [.. page.Value.Items.Select(EventJournalEntryDto.From)],
            page.Value.Page,
            page.Value.PageSize,
            page.Value.TotalCount,
            page.Value.HasNextPage,
            head.Value
        );
        return Ok(new StatusResponseDto<EventJournalPageDto> { Status = "ok", Data = body });
    }

    /// <summary>
    /// Exports the channel's whole journal as a JSONL file download (one event envelope per line). Owner-only.
    /// </summary>
    [RequireAction("eventstore:export")]
    [HttpPost("export")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(string channelId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
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
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [ProducesResponseType<StatusResponseDto<EventJournalImportSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Import(string channelId, IFormFile file, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
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
    /// Replays every imported-but-not-yet-replayed event for the channel, in their original order, through the
    /// SAME live-handler path a real Twitch event takes — so pipeline triggers, TTS-enabled commands, and reward
    /// handling actually re-run for the imported history instead of just sitting silently in the journal. Owner-
    /// only. Idempotent: only advances past NEW imported rows since the last replay, so calling it again after
    /// nothing new was imported is a safe no-op. Cannot touch another channel's events — the runner reads only
    /// this resolved tenant's own stream (event-store §3.3).
    /// </summary>
    [RequireAction("eventstore:replay:write")]
    [HttpPost("replay")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [ProducesResponseType<StatusResponseDto<long>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Replay(string channelId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No channel resolved for the caller.");

        Result<long> result = await _projectionRunner.RunOnceAsync(
            "import-replay",
            broadcasterId,
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
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [ProducesResponseType<StatusResponseDto<LegacyImportResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportLegacy(string channelId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
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
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ProjectionRebuildResult>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> RebuildProjections(string channelId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No channel resolved for the caller.");

        Result<IReadOnlyList<ProjectionRebuildResult>> result =
            await _legacyImport.RebuildProjectionsAsync(broadcasterId, ct);
        return ResultResponse(result);
    }
}
