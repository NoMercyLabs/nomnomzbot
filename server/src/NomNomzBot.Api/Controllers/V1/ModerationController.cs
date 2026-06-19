// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/moderation")]
[Authorize]
[Tags("Moderation")]
public class ModerationController : BaseController
{
    private readonly IModerationService _moderationService;
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ModerationController(
        IModerationService moderationService,
        IApplicationDbContext db,
        TimeProvider timeProvider
    )
    {
        _moderationService = moderationService;
        _db = db;
        _timeProvider = timeProvider;
    }

    // ─── Rules ───────────────────────────────────────────────────────────────

    [HttpGet("rules")]
    [ProducesResponseType<PaginatedResponse<ModerationRuleDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRules(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<ModerationRuleListItem>> result = await _moderationService.ListRulesAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpPost("rules")]
    [ProducesResponseType<StatusResponseDto<ModerationRuleDetail>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateRule(
        string channelId,
        [FromBody] CreateModerationRuleRequest request,
        CancellationToken ct
    )
    {
        Result<ModerationRuleDetail> result = await _moderationService.CreateRuleAsync(
            channelId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(ListRules),
            new { channelId },
            new StatusResponseDto<ModerationRuleDetail>
            {
                Data = result.Value,
                Message = "Rule created successfully.",
            }
        );
    }

    [HttpPut("rules/{ruleId:int}")]
    [ProducesResponseType<StatusResponseDto<ModerationRuleDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRule(
        string channelId,
        int ruleId,
        [FromBody] UpdateModerationRuleRequest request,
        CancellationToken ct
    )
    {
        Result<ModerationRuleDetail> result = await _moderationService.UpdateRuleAsync(
            channelId,
            ruleId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<ModerationRuleDetail> { Data = result.Value });
    }

    [HttpDelete("rules/{ruleId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteRule(string channelId, int ruleId, CancellationToken ct)
    {
        Result result = await _moderationService.DeleteRuleAsync(channelId, ruleId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    // ─── AutoMod Config ──────────────────────────────────────────────────────

    [HttpGet("automod")]
    [ProducesResponseType<StatusResponseDto<AutomodConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAutomodConfig(string channelId, CancellationToken ct)
    {
        Result<AutomodConfigDto> result = await _moderationService.GetAutomodConfigAsync(
            channelId,
            ct
        );
        return ResultResponse(result);
    }

    [HttpPost("automod")]
    [ProducesResponseType<StatusResponseDto<AutomodConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveAutomodConfig(
        string channelId,
        [FromBody] AutomodConfigDto request,
        CancellationToken ct
    )
    {
        Result<AutomodConfigDto> result = await _moderationService.SaveAutomodConfigAsync(
            channelId,
            request,
            ct
        );
        return ResultResponse(result);
    }

    // ─── Bans ─────────────────────────────────────────────────────────────────

    [HttpGet("bans")]
    [ProducesResponseType<StatusResponseDto<List<BannedUserDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBannedUsers(string channelId, CancellationToken ct)
    {
        Result<List<BannedUserDto>> result = await _moderationService.GetBannedUsersAsync(
            channelId,
            ct
        );
        return ResultResponse(result);
    }

    [HttpDelete("bans/{userId}")]
    [ProducesResponseType<StatusResponseDto<ModerationActionResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnbanUser(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        Result<ModerationActionResult> result = await _moderationService.UnbanAsync(
            channelId,
            userId,
            ct
        );
        return ResultResponse(result);
    }

    // ─── Mod Log ─────────────────────────────────────────────────────────────

    [HttpGet("log")]
    [ProducesResponseType<PaginatedResponse<ModLogEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetModLog(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<ModerationActionLog>> result = await _moderationService.GetActionsAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);

        PagedList<ModLogEntryDto> mapped = new(
            result
                .Value.Items.Select(a => new ModLogEntryDto(
                    a.Id,
                    a.Action,
                    a.ModeratorUsername,
                    a.TargetUsername,
                    a.Reason,
                    a.Timestamp,
                    a.DurationSeconds
                ))
                .ToList(),
            result.Value.TotalCount,
            result.Value.Page,
            result.Value.PageSize
        );
        return GetPaginatedResponse(mapped, request);
    }

    // ─── Shield Mode ─────────────────────────────────────────────────────────

    [HttpGet("shield")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetShieldMode(string channelId, CancellationToken ct)
    {
        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == channelId && c.Key == "shield.mode",
            ct
        );

        bool enabled = cfg?.Value is not null && bool.TryParse(cfg.Value, out bool v) && v;
        return Ok(new StatusResponseDto<object> { Data = new { enabled } });
    }

    [HttpPatch("shield")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetShieldMode(
        string channelId,
        [FromBody] SetShieldRequest request,
        CancellationToken ct
    )
    {
        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == channelId && c.Key == "shield.mode",
            ct
        );

        if (cfg is null)
        {
            cfg = new ConfigEntity
            {
                BroadcasterId = channelId,
                Key = "shield.mode",
                Value = request.Enabled.ToString(),
            };
            _db.Configurations.Add(cfg);
        }
        else
        {
            cfg.Value = request.Enabled.ToString();
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new StatusResponseDto<object> { Data = new { enabled = request.Enabled } });
    }

    public record SetShieldRequest(bool Enabled);

    // ─── Blocked Terms ────────────────────────────────────────────────────────

    [HttpGet("blocked-terms")]
    [ProducesResponseType<StatusResponseDto<List<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlockedTerms(string channelId, CancellationToken ct)
    {
        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == channelId && c.Key == "blocked-terms",
            ct
        );

        List<string> terms = cfg?.Value is not null
            ? JsonSerializer.Deserialize<List<string>>(cfg.Value) ?? []
            : [];

        return Ok(new StatusResponseDto<List<string>> { Data = terms });
    }

    [HttpPost("blocked-terms")]
    [ProducesResponseType<StatusResponseDto<List<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddBlockedTerm(
        string channelId,
        [FromBody] AddTermRequest request,
        CancellationToken ct
    )
    {
        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == channelId && c.Key == "blocked-terms",
            ct
        );

        List<string> terms = cfg?.Value is not null
            ? JsonSerializer.Deserialize<List<string>>(cfg.Value) ?? []
            : [];

        if (!terms.Contains(request.Term, StringComparer.OrdinalIgnoreCase))
            terms.Add(request.Term);

        if (cfg is null)
        {
            cfg = new ConfigEntity
            {
                BroadcasterId = channelId,
                Key = "blocked-terms",
                Value = JsonSerializer.Serialize(terms),
            };
            _db.Configurations.Add(cfg);
        }
        else
        {
            cfg.Value = JsonSerializer.Serialize(terms);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new StatusResponseDto<List<string>> { Data = terms });
    }

    [HttpDelete("blocked-terms/{term}")]
    [ProducesResponseType<StatusResponseDto<List<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveBlockedTerm(
        string channelId,
        string term,
        CancellationToken ct
    )
    {
        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == channelId && c.Key == "blocked-terms",
            ct
        );

        if (cfg is null)
            return Ok(new StatusResponseDto<List<string>> { Data = [] });

        List<string> terms = JsonSerializer.Deserialize<List<string>>(cfg.Value ?? "[]") ?? [];
        terms.RemoveAll(t => string.Equals(t, term, StringComparison.OrdinalIgnoreCase));
        cfg.Value = JsonSerializer.Serialize(terms);
        await _db.SaveChangesAsync(ct);
        return Ok(new StatusResponseDto<List<string>> { Data = terms });
    }

    public record AddTermRequest(string Term);

    // ─── Stats ────────────────────────────────────────────────────────────────

    [HttpGet("stats")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(string channelId, CancellationToken ct)
    {
        DateTime today = _timeProvider.GetUtcNow().UtcDateTime.Date;

        var events = await _db
            .ChannelEvents.Where(e => e.ChannelId == channelId && e.CreatedAt >= today)
            .Select(e => e.Type)
            .ToListAsync(ct);

        int bansToday = events.Count(t => t.Contains("ban", StringComparison.OrdinalIgnoreCase));
        int timeouts = events.Count(t => t.Contains("timeout", StringComparison.OrdinalIgnoreCase));
        int deletedMessages = events.Count(t =>
            t.Contains("delete", StringComparison.OrdinalIgnoreCase)
        );
        int automodActions = events.Count(t =>
            t.Contains("automod", StringComparison.OrdinalIgnoreCase)
        );

        return Ok(
            new StatusResponseDto<object>
            {
                Data = new
                {
                    bansToday,
                    timeouts,
                    deletedMessages,
                    automodActions,
                },
            }
        );
    }

    // ─── Actions ─────────────────────────────────────────────────────────────

    [HttpGet("actions")]
    [ProducesResponseType<PaginatedResponse<ModerationActionResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListActions(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<ModerationActionLog>> result = await _moderationService.GetActionsAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpPost("actions")]
    [ProducesResponseType<StatusResponseDto<ModerationActionResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PerformAction(
        string channelId,
        [FromBody] PerformModerationActionRequest request,
        CancellationToken ct
    )
    {
        Result<ModerationActionResult> result = request.Action switch
        {
            "timeout" => await _moderationService.TimeoutAsync(
                channelId,
                request.TargetUserId,
                request.DurationSeconds ?? 600,
                request.Reason,
                ct
            ),

            "ban" => await _moderationService.BanAsync(
                channelId,
                request.TargetUserId,
                request.Reason,
                ct
            ),

            "unban" => await _moderationService.UnbanAsync(channelId, request.TargetUserId, ct),

            _ => Result.Failure<ModerationActionResult>(
                $"Unknown action '{request.Action}'. Supported: timeout, ban, unban.",
                "VALIDATION_FAILED"
            ),
        };

        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<ModerationActionResult> { Data = result.Value });
    }
}
