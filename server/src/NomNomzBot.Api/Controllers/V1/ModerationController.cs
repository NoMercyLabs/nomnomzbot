// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
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
    private readonly ITwitchChatApi _chatApi;
    private readonly IEventBus _eventBus;

    public ModerationController(
        IModerationService moderationService,
        IApplicationDbContext db,
        TimeProvider timeProvider,
        ITwitchChatApi chatApi,
        IEventBus eventBus
    )
    {
        _moderationService = moderationService;
        _db = db;
        _timeProvider = timeProvider;
        _chatApi = chatApi;
        _eventBus = eventBus;
    }

    // ─── Rules ───────────────────────────────────────────────────────────────

    [RequireAction("moderation:filter:read")]
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

    [RequireAction("moderation:filter:write")]
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

    [RequireAction("moderation:filter:write")]
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

    [RequireAction("moderation:filter:write")]
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

    [RequireAction("moderation:automod:read")]
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

    [RequireAction("moderation:automod:write")]
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

    [RequireAction("moderation:read")]
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

    [RequireAction("moderation:unban")]
    [HttpDelete("bans/{userId}")]
    [ProducesResponseType<StatusResponseDto<ModerationActionResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnbanUser(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        string actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        Result<ModerationActionResult> result = await _moderationService.UnbanAsync(
            channelId,
            userId,
            actorId,
            ct
        );
        return ResultResponse(result);
    }

    // ─── Mod Log ─────────────────────────────────────────────────────────────

    [RequireAction("moderation:action:read")]
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

    [RequireAction("moderation:shieldmode:read")]
    [HttpGet("shield")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetShieldMode(string channelId, CancellationToken ct)
    {
        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == "shield.mode",
            ct
        );

        bool enabled = cfg?.Value is not null && bool.TryParse(cfg.Value, out bool v) && v;
        return Ok(new StatusResponseDto<object> { Data = new { enabled } });
    }

    [RequireAction("moderation:shieldmode:write")]
    [HttpPatch("shield")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetShieldMode(
        string channelId,
        [FromBody] SetShieldRequest request,
        CancellationToken ct
    )
    {
        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == "shield.mode",
            ct
        );

        if (cfg is null)
        {
            cfg = new ConfigEntity
            {
                BroadcasterId = broadcasterId,
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
        await PublishConfigChangedAsync(
            broadcasterId ?? Guid.Empty,
            "moderation-rules",
            "shield-mode",
            "updated",
            ct
        );
        return Ok(new StatusResponseDto<object> { Data = new { enabled = request.Enabled } });
    }

    public record SetShieldRequest(bool Enabled);

    // ─── Blocked Terms ────────────────────────────────────────────────────────

    [RequireAction("moderation:filter:read")]
    [HttpGet("blocked-terms")]
    [ProducesResponseType<StatusResponseDto<List<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlockedTerms(string channelId, CancellationToken ct)
    {
        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == "blocked-terms",
            ct
        );

        List<string> terms = cfg?.Value is not null
            ? JsonSerializer.Deserialize<List<string>>(cfg.Value) ?? []
            : [];

        return Ok(new StatusResponseDto<List<string>> { Data = terms });
    }

    [RequireAction("moderation:blocklist:write")]
    [HttpPost("blocked-terms")]
    [ProducesResponseType<StatusResponseDto<List<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddBlockedTerm(
        string channelId,
        [FromBody] AddTermRequest request,
        CancellationToken ct
    )
    {
        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == "blocked-terms",
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
                BroadcasterId = broadcasterId,
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
        await PublishConfigChangedAsync(
            broadcasterId ?? Guid.Empty,
            "blocked-terms",
            request.Term,
            "created",
            ct
        );
        return Ok(new StatusResponseDto<List<string>> { Data = terms });
    }

    [RequireAction("moderation:blocklist:write")]
    [HttpDelete("blocked-terms/{term}")]
    [ProducesResponseType<StatusResponseDto<List<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveBlockedTerm(
        string channelId,
        string term,
        CancellationToken ct
    )
    {
        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == "blocked-terms",
            ct
        );

        if (cfg is null)
            return Ok(new StatusResponseDto<List<string>> { Data = [] });

        List<string> terms = JsonSerializer.Deserialize<List<string>>(cfg.Value ?? "[]") ?? [];
        terms.RemoveAll(t => string.Equals(t, term, StringComparison.OrdinalIgnoreCase));
        cfg.Value = JsonSerializer.Serialize(terms);
        await _db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(
            broadcasterId ?? Guid.Empty,
            "blocked-terms",
            term,
            "deleted",
            ct
        );
        return Ok(new StatusResponseDto<List<string>> { Data = terms });
    }

    public record AddTermRequest(string Term);

    // ─── Stats ────────────────────────────────────────────────────────────────

    [RequireAction("moderation:read")]
    [HttpGet("stats")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(string channelId, CancellationToken ct)
    {
        DateTime today = _timeProvider.GetUtcNow().UtcDateTime.Date;

        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        List<string> events = await _db
            .ChannelEvents.Where(e => e.ChannelId == broadcasterId && e.CreatedAt >= today)
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

    [RequireAction("moderation:action:read")]
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

    [RequireAction("moderation:ban")]
    [HttpPost("actions")]
    [ProducesResponseType<StatusResponseDto<ModerationActionResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PerformAction(
        string channelId,
        [FromBody] PerformModerationActionRequest request,
        CancellationToken ct
    )
    {
        string actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        Result<ModerationActionResult> result = request.Action switch
        {
            "timeout" => await _moderationService.TimeoutAsync(
                channelId,
                request.TargetUserId,
                request.DurationSeconds ?? 600,
                request.Reason,
                actorId,
                ct
            ),

            "ban" => await _moderationService.BanAsync(
                channelId,
                request.TargetUserId,
                request.Reason,
                actorId,
                ct
            ),

            "unban" => await _moderationService.UnbanAsync(
                channelId,
                request.TargetUserId,
                actorId,
                ct
            ),

            _ => Result.Failure<ModerationActionResult>(
                $"Unknown action '{request.Action}'. Supported: timeout, ban, unban.",
                "VALIDATION_FAILED"
            ),
        };

        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<ModerationActionResult> { Data = result.Value });
    }

    // ─── Shoutout ─────────────────────────────────────────────────────────────

    public record ShoutoutRequest(string TargetTwitchUserId);

    [RequireAction("moderation:shoutout")]
    [HttpPost("shoutout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Shoutout(
        string channelId,
        [FromBody] ShoutoutRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _chatApi.SendShoutoutAsync(
            broadcasterId,
            request.TargetTwitchUserId,
            ct
        );
        return result.IsFailure ? TwitchResultResponse(result) : NoContent();
    }

    /// <summary>
    /// E5 dashboard live-sync: shield mode and blocked terms have no dedicated service (their CRUD lives here),
    /// so this is the one place in the controller layer that publishes — everywhere else the publish happens in
    /// the service layer. Fired after every successful write so other open dashboards refetch.
    /// </summary>
    private Task PublishConfigChangedAsync(
        Guid broadcasterId,
        string domain,
        string? entityId,
        string action,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = domain,
                EntityId = entityId,
                Action = action,
            },
            ct
        );
}
