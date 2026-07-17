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
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Per-channel moderation endpoints for a broadcaster or their moderators — banning, timing out, and unbanning
/// viewers via Twitch, tuning the bot's own auto-moderation rules and blocked-term list, and reviewing moderation
/// history and stats.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/moderation")]
[Authorize]
[Tags("Moderation")]
public class ModerationController : BaseController
{
    private readonly IModerationService _moderationService;
    private readonly IOperatorNetworkBanService _networkBan;
    private readonly IViewerReportService _reports;
    private readonly ISharedBanService _sharedBans;
    private readonly INetworkNukeService _nuke;
    private readonly IModerationEscalationService _escalation;
    private readonly ICurrentUserService _currentUser;
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ITwitchChatApi _chatApi;

    public ModerationController(
        IModerationService moderationService,
        IOperatorNetworkBanService networkBan,
        IViewerReportService reports,
        ISharedBanService sharedBans,
        INetworkNukeService nuke,
        IModerationEscalationService escalation,
        ICurrentUserService currentUser,
        IApplicationDbContext db,
        TimeProvider timeProvider,
        ITwitchChatApi chatApi
    )
    {
        _moderationService = moderationService;
        _networkBan = networkBan;
        _reports = reports;
        _sharedBans = sharedBans;
        _nuke = nuke;
        _escalation = escalation;
        _currentUser = currentUser;
        _db = db;
        _timeProvider = timeProvider;
        _chatApi = chatApi;
    }

    // ─── Ban (this channel, or every channel the operator moderates) ───────────

    /// <summary>
    /// Ban a viewer — in THIS channel (a permanent ban, or a timeout when <c>DurationSeconds</c> is set) or across
    /// EVERY channel the operator moderates (chat-client.md §3.5, <c>Scope = "all_moderated"</c>). Both scopes return
    /// a <see cref="NetworkBanResultDto"/>; <c>this_channel</c> is a one-row result. The <c>all_moderated</c> sweep is
    /// best-effort per channel and issues each ban AS THE OPERATOR (their own token), so Twitch — not us — decides
    /// where the ban is permitted.
    /// </summary>
    [RequireAction("moderation:ban")]
    [HttpPost("actions/ban")]
    [ProducesResponseType<StatusResponseDto<NetworkBanResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BanUser(
        string channelId,
        [FromBody] BanUserRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        if (request.Scope == "all_moderated")
        {
            Result<NetworkBanResult> fanOut = await _networkBan.BanAcrossModeratedAsync(
                operatorUserId,
                request.TargetTwitchUserId,
                request.Reason,
                ct
            );
            if (fanOut.IsFailure)
                return ResultResponse(fanOut);

            return Ok(new StatusResponseDto<NetworkBanResultDto> { Data = ToDto(fanOut.Value) });
        }

        // this_channel — a permanent ban, or a timeout when a duration is supplied.
        Result<ModerationActionResult> single = request.DurationSeconds is int seconds
            ? await _moderationService.TimeoutAsync(
                channelId,
                operatorUserId,
                request.TargetTwitchUserId,
                seconds,
                request.Reason,
                cancellationToken: ct
            )
            : await _moderationService.BanAsync(
                channelId,
                operatorUserId,
                request.TargetTwitchUserId,
                request.Reason,
                cancellationToken: ct
            );
        if (single.IsFailure)
            return ResultResponse(single);

        string login = await ResolveChannelLoginAsync(channelId, ct);
        NetworkBanResultDto oneRow = new(1, 1, [new ChannelBanOutcomeDto(login, true, null)]);
        return Ok(new StatusResponseDto<NetworkBanResultDto> { Data = oneRow });
    }

    /// <summary>
    /// Unban a viewer — in THIS channel or across EVERY channel the operator moderates (the reversal of the network
    /// ban, <c>Scope = "all_moderated"</c>). Both scopes return a <see cref="NetworkBanResultDto"/>; <c>this_channel</c>
    /// is a one-row result. The <c>all_moderated</c> sweep is best-effort per channel and lifts each ban AS THE
    /// OPERATOR (their own token), so Twitch — not us — decides where it is permitted.
    /// </summary>
    [RequireAction("moderation:unban")]
    [HttpPost("actions/unban")]
    [ProducesResponseType<StatusResponseDto<NetworkBanResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnbanUserScoped(
        string channelId,
        [FromBody] UnbanUserRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        if (request.Scope == "all_moderated")
        {
            Result<NetworkBanResult> fanOut = await _networkBan.UnbanAcrossModeratedAsync(
                operatorUserId,
                request.TargetTwitchUserId,
                ct
            );
            if (fanOut.IsFailure)
                return ResultResponse(fanOut);

            return Ok(new StatusResponseDto<NetworkBanResultDto> { Data = ToDto(fanOut.Value) });
        }

        // this_channel — a single unban.
        string actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        Result<ModerationActionResult> single = await _moderationService.UnbanAsync(
            channelId,
            operatorUserId,
            request.TargetTwitchUserId,
            actorId,
            ct
        );
        if (single.IsFailure)
            return ResultResponse(single);

        string login = await ResolveChannelLoginAsync(channelId, ct);
        NetworkBanResultDto oneRow = new(1, 1, [new ChannelBanOutcomeDto(login, true, null)]);
        return Ok(new StatusResponseDto<NetworkBanResultDto> { Data = oneRow });
    }

    private static NetworkBanResultDto ToDto(NetworkBanResult result) =>
        new(
            result.Attempted,
            result.Succeeded,
            result
                .Channels.Select(channel => new ChannelBanOutcomeDto(
                    channel.BroadcasterLogin,
                    channel.Succeeded,
                    channel.Error
                ))
                .ToList()
        );

    private async Task<string> ResolveChannelLoginAsync(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid id))
            return channelId;
        return await _db
                .Channels.Where(channel => channel.Id == id)
                .Select(channel => channel.Name)
                .FirstOrDefaultAsync(ct)
            ?? channelId;
    }

    // ─── Rules ───────────────────────────────────────────────────────────────

    /// <summary>List the channel's auto-moderation rules, paginated.</summary>
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

    /// <summary>Create a new auto-moderation rule for the channel.</summary>
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

    /// <summary>Update an existing auto-moderation rule.</summary>
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

    /// <summary>Delete an auto-moderation rule.</summary>
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

    /// <summary>Get the channel's auto-moderation config (link filter, caps filter, banned phrases, emote spam).</summary>
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

    /// <summary>Save the channel's auto-moderation config, upserting its built-in filter rules.</summary>
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

    /// <summary>List users currently banned in the channel, via the Twitch moderation API.</summary>
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

    /// <summary>Unban a user from the channel via the Twitch moderation API.</summary>
    [RequireAction("moderation:unban")]
    [HttpDelete("bans/{userId}")]
    [ProducesResponseType<StatusResponseDto<ModerationActionResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnbanUser(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        string actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        Result<ModerationActionResult> result = await _moderationService.UnbanAsync(
            channelId,
            operatorUserId,
            userId,
            actorId,
            ct
        );
        return ResultResponse(result);
    }

    // ─── Mod Log ─────────────────────────────────────────────────────────────

    /// <summary>Get the channel's moderation action log, paginated, with moderator and target usernames resolved.</summary>
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

    /// <summary>Check whether the channel's Shield Mode is currently active, live from the Twitch moderation API.</summary>
    [RequireAction("moderation:shieldmode:read")]
    [HttpGet("shield")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetShieldMode(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result<bool> result = await _moderationService.GetShieldModeAsync(
            channelId,
            operatorUserId,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<object> { Data = new { enabled = result.Value } });
    }

    /// <summary>Turn the channel's Shield Mode on or off via the Twitch moderation API and notify open dashboards.</summary>
    [RequireAction("moderation:shieldmode:write")]
    [HttpPatch("shield")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetShieldMode(
        string channelId,
        [FromBody] SetShieldRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result<bool> result = await _moderationService.SetShieldModeAsync(
            channelId,
            operatorUserId,
            request.Enabled,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<object> { Data = new { enabled = result.Value } });
    }

    public record SetShieldRequest(bool Enabled);

    // ─── Blocked Terms ────────────────────────────────────────────────────────

    /// <summary>List the channel's blocked terms, live from the Twitch moderation API.</summary>
    [RequireAction("moderation:filter:read")]
    [HttpGet("blocked-terms")]
    [ProducesResponseType<StatusResponseDto<List<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlockedTerms(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result<List<string>> result = await _moderationService.GetBlockedTermsAsync(
            channelId,
            operatorUserId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Add a term to the channel's blocked-terms list via the Twitch moderation API.</summary>
    [RequireAction("moderation:blocklist:write")]
    [HttpPost("blocked-terms")]
    [ProducesResponseType<StatusResponseDto<List<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddBlockedTerm(
        string channelId,
        [FromBody] AddTermRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result<List<string>> result = await _moderationService.AddBlockedTermAsync(
            channelId,
            operatorUserId,
            request.Term,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Remove a term from the channel's blocked-terms list via the Twitch moderation API.</summary>
    [RequireAction("moderation:blocklist:write")]
    [HttpDelete("blocked-terms/{term}")]
    [ProducesResponseType<StatusResponseDto<List<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveBlockedTerm(
        string channelId,
        string term,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result<List<string>> result = await _moderationService.RemoveBlockedTermAsync(
            channelId,
            operatorUserId,
            term,
            ct
        );
        return ResultResponse(result);
    }

    public record AddTermRequest(string Term);

    // ─── Unban requests ───────────────────────────────────────────────────────

    /// <summary>List the channel's unban requests (default: the pending queue), live from the Twitch moderation API.</summary>
    [RequireAction("moderation:unbanrequest:read")]
    [HttpGet("unban-requests")]
    [ProducesResponseType<StatusResponseDto<List<UnbanRequestDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnbanRequests(
        string channelId,
        [FromQuery] string status,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result<List<UnbanRequestDto>> result = await _moderationService.GetUnbanRequestsAsync(
            channelId,
            operatorUserId,
            string.IsNullOrWhiteSpace(status) ? "pending" : status,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Approve or deny an unban request via the Twitch moderation API.</summary>
    [RequireAction("moderation:unbanrequest:resolve")]
    [HttpPost("unban-requests/{unbanRequestId}/resolve")]
    [ProducesResponseType<StatusResponseDto<UnbanRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveUnbanRequest(
        string channelId,
        string unbanRequestId,
        [FromBody] ResolveUnbanRequestRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result<UnbanRequestDto> result = await _moderationService.ResolveUnbanRequestAsync(
            channelId,
            operatorUserId,
            unbanRequestId,
            request.Approve,
            request.Note,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<UnbanRequestDto> { Data = result.Value });
    }

    // ─── Per-user enforcement (warn / suspicious) ─────────────────────────────

    /// <summary>Warn a chatter via the Twitch moderation API — they must acknowledge it before chatting again.</summary>
    [RequireAction("moderation:warn")]
    [HttpPost("warn")]
    [ProducesResponseType<StatusResponseDto<ModerationActionResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> WarnUser(
        string channelId,
        [FromBody] WarnUserRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        string actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        Result<ModerationActionResult> result = await _moderationService.WarnUserAsync(
            channelId,
            operatorUserId,
            request.TargetUserId,
            request.Reason,
            actorId,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<ModerationActionResult> { Data = result.Value });
    }

    /// <summary>Flag a chatter as suspicious (active_monitoring or restricted) via the Twitch moderation API.</summary>
    [RequireAction("moderation:suspicioususer:write")]
    [HttpPost("suspicious")]
    [ProducesResponseType<StatusResponseDto<SuspiciousStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetSuspiciousStatus(
        string channelId,
        [FromBody] SetSuspiciousStatusRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result<SuspiciousStatusDto> result = await _moderationService.SetSuspiciousStatusAsync(
            channelId,
            operatorUserId,
            request.TargetUserId,
            request.Status,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<SuspiciousStatusDto> { Data = result.Value });
    }

    /// <summary>Clear a chatter's suspicious-user flag via the Twitch moderation API.</summary>
    [RequireAction("moderation:suspicioususer:write")]
    [HttpDelete("suspicious/{userId}")]
    [ProducesResponseType<StatusResponseDto<SuspiciousStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearSuspiciousStatus(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result<SuspiciousStatusDto> result = await _moderationService.ClearSuspiciousStatusAsync(
            channelId,
            operatorUserId,
            userId,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<SuspiciousStatusDto> { Data = result.Value });
    }

    /// <summary>The channel's escalation ladder policy (moderation.md §3.11, J.10) — the default disabled ladder when unset.</summary>
    [RequireAction("moderation:escalation:read")]
    [HttpGet("escalation")]
    [ProducesResponseType<StatusResponseDto<ModerationEscalationPolicyDto>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetEscalationPolicy(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return ResultResponse(Result.Failure("Invalid channel id.", "VALIDATION_FAILED"));
        return ResultResponse(await _escalation.GetPolicyAsync(broadcaster, ct));
    }

    /// <summary>Saves the escalation ladder (whole ladder replaced; steps strictly ascending). SuperMod tier.</summary>
    [RequireAction("moderation:escalation:write")]
    [HttpPut("escalation")]
    [ProducesResponseType<StatusResponseDto<ModerationEscalationPolicyDto>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> UpsertEscalationPolicy(
        string channelId,
        [FromBody] UpsertEscalationPolicyRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return ResultResponse(Result.Failure("Invalid channel id.", "VALIDATION_FAILED"));
        return ResultResponse(await _escalation.UpsertPolicyAsync(broadcaster, request, ct));
    }

    /// <summary>Forgiveness — clears the viewer's ladder tally so their next offense starts at rung one.</summary>
    [RequireAction("moderation:escalation:write")]
    [HttpPost("escalation/users/{userId:guid}/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetEscalation(
        string channelId,
        Guid userId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return ResultResponse(Result.Failure("Invalid channel id.", "VALIDATION_FAILED"));
        Result result = await _escalation.ResetUserAsync(broadcaster, userId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    /// <summary>
    /// The SuperMod platform nuke (moderation.md §3.4): bans the target across every tenant channel the
    /// actor holds SuperMod+ on. Requires explicit confirmation in the request body.
    /// </summary>
    [RequireAction("moderation:nuke")]
    [HttpPost("nuke")]
    [ProducesResponseType<StatusResponseDto<NetworkNukeBatchDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Nuke(
        string channelId,
        [FromBody] NetworkNukeRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return ResultResponse(Result.Failure("Invalid channel id.", "VALIDATION_FAILED"));
        if (!Guid.TryParse(_currentUser.UserId, out Guid actorUserId))
            return UnauthenticatedResponse();
        return ResultResponse(await _nuke.NukeAsync(broadcaster, actorUserId, request, ct));
    }

    /// <summary>The one-shot reversal (un-nuke) of a whole batch.</summary>
    [RequireAction("moderation:nuke")]
    [HttpPost("nuke/{batchId:guid}/revert")]
    [ProducesResponseType<StatusResponseDto<NetworkNukeBatchDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevertNuke(
        string channelId,
        Guid batchId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid actorUserId))
            return UnauthenticatedResponse();
        return ResultResponse(await _nuke.RevertAsync(actorUserId, batchId, ct));
    }

    /// <summary>The origin channel's nuke-batch history, newest first.</summary>
    [RequireAction("moderation:nuke:read")]
    [HttpGet("nuke")]
    [ProducesResponseType<PaginatedResponse<NetworkNukeBatchDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListNukeBatches(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return ResultResponse(Result.Failure("Invalid channel id.", "VALIDATION_FAILED"));
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<NetworkNukeBatchDto>> result = await _nuke.ListBatchesAsync(
            broadcaster,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>The channel's shared-chat ban policy + trust list (moderation.md §3.5, J.9/J.9a).</summary>
    [RequireAction("moderation:sharedban:read")]
    [HttpGet("shared-bans")]
    [ProducesResponseType<StatusResponseDto<SharedBanSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSharedBanSettings(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return ResultResponse(Result.Failure("Invalid channel id.", "VALIDATION_FAILED"));
        return ResultResponse(await _sharedBans.GetSettingsAsync(broadcaster, ct));
    }

    /// <summary>Saves the shared-ban policy (both opt-in switches explicit). SuperMod tier.</summary>
    [RequireAction("moderation:sharedban:write")]
    [HttpPut("shared-bans")]
    [ProducesResponseType<StatusResponseDto<SharedBanSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSharedBanSettings(
        string channelId,
        [FromBody] SaveSharedBanSettingsRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return ResultResponse(Result.Failure("Invalid channel id.", "VALIDATION_FAILED"));
        if (!Guid.TryParse(_currentUser.UserId, out Guid actorUserId))
            return UnauthenticatedResponse();
        return ResultResponse(
            await _sharedBans.SaveSettingsAsync(broadcaster, actorUserId, request, ct)
        );
    }

    /// <summary>Adds a partner channel to the inbound-ban trust list (idempotent). SuperMod tier.</summary>
    [RequireAction("moderation:sharedban:write")]
    [HttpPost("shared-bans/trusted")]
    [ProducesResponseType<StatusResponseDto<SharedBanTrustedChannelDto>>(
        StatusCodes.Status201Created
    )]
    public async Task<IActionResult> AddSharedBanTrustedChannel(
        string channelId,
        [FromBody] AddTrustedChannelRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return ResultResponse(Result.Failure("Invalid channel id.", "VALIDATION_FAILED"));
        if (!Guid.TryParse(_currentUser.UserId, out Guid actorUserId))
            return UnauthenticatedResponse();
        Result<SharedBanTrustedChannelDto> result = await _sharedBans.AddTrustedChannelAsync(
            broadcaster,
            actorUserId,
            request.TrustedChannelId,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return StatusCode(
            StatusCodes.Status201Created,
            new StatusResponseDto<SharedBanTrustedChannelDto> { Data = result.Value }
        );
    }

    /// <summary>Removes a partner channel from the trust list. SuperMod tier.</summary>
    [RequireAction("moderation:sharedban:write")]
    [HttpDelete("shared-bans/trusted/{trustedChannelId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveSharedBanTrustedChannel(
        string channelId,
        Guid trustedChannelId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return ResultResponse(Result.Failure("Invalid channel id.", "VALIDATION_FAILED"));
        if (!Guid.TryParse(_currentUser.UserId, out Guid actorUserId))
            return UnauthenticatedResponse();
        Result result = await _sharedBans.RemoveTrustedChannelAsync(
            broadcaster,
            actorUserId,
            trustedChannelId,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    /// <summary>
    /// Set a viewer's BOT-SIDE standing (J.12): muted (features ignore them, chat still shows),
    /// shadowbanned (muted + hidden from overlays), or blacklisted (dropped entirely). Never touches
    /// Twitch — ban/timeout stay the Twitch-native punishments.
    /// </summary>
    [RequireAction("moderation:suspicioususer:write")]
    [HttpPost("users/{userId}/standing")]
    [ProducesResponseType<StatusResponseDto<ModerationStandingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetModerationStanding(
        string channelId,
        string userId,
        [FromBody] SetModerationStandingRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result<ModerationStandingDto> result = await _moderationService.SetModerationStandingAsync(
            channelId,
            operatorUserId,
            userId,
            request.Provider,
            request.Standing,
            request.Reason,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<ModerationStandingDto> { Data = result.Value });
    }

    /// <summary>Clear a viewer's bot-side standing back to normal.</summary>
    [RequireAction("moderation:suspicioususer:write")]
    [HttpDelete("users/{userId}/standing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearModerationStanding(
        string channelId,
        string userId,
        [FromQuery] string provider,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result result = await _moderationService.ClearModerationStandingAsync(
            channelId,
            operatorUserId,
            userId,
            provider,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    /// <summary>
    /// The per-user moderation summary for the mod panel — the bot's recorded actions against this viewer
    /// (ban/timeout/warn/unban counts + recent actions). The bot's own history, not Twitch's complete record.
    /// </summary>
    [RequireAction("moderation:usercontext:read")]
    [HttpGet("users/{userId}/context")]
    [ProducesResponseType<StatusResponseDto<UserModerationContextDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserContext(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        Result<UserModerationContextDto> result = await _moderationService.GetUserContextAsync(
            channelId,
            userId,
            ct
        );
        return ResultResponse(result);
    }

    // ─── User notes (mod panel) ────────────────────────────────────────────────

    /// <summary>List the moderator notes about a viewer (pinned first, then most recent).</summary>
    [RequireAction("moderation:usercontext:read")]
    [HttpGet("users/{userId}/notes")]
    [ProducesResponseType<StatusResponseDto<List<UserNoteDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUserNotes(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        Result<List<UserNoteDto>> result = await _moderationService.ListUserNotesAsync(
            channelId,
            userId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Add a moderator note about a viewer, attributed to the acting moderator.</summary>
    [RequireAction("moderation:note:write")]
    [HttpPost("users/{userId}/notes")]
    [ProducesResponseType<StatusResponseDto<UserNoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddUserNote(
        string channelId,
        string userId,
        [FromBody] CreateUserNoteRequest request,
        CancellationToken ct
    )
    {
        string actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        Result<UserNoteDto> result = await _moderationService.AddUserNoteAsync(
            channelId,
            userId,
            request,
            actorId,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<UserNoteDto> { Data = result.Value });
    }

    /// <summary>Edit a moderator note's text and/or pinned state.</summary>
    [RequireAction("moderation:note:write")]
    [HttpPut("notes/{noteId:int}")]
    [ProducesResponseType<StatusResponseDto<UserNoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateUserNote(
        string channelId,
        int noteId,
        [FromBody] UpdateUserNoteRequest request,
        CancellationToken ct
    )
    {
        Result<UserNoteDto> result = await _moderationService.UpdateUserNoteAsync(
            channelId,
            noteId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<UserNoteDto> { Data = result.Value });
    }

    /// <summary>Delete a moderator note.</summary>
    [RequireAction("moderation:note:write")]
    [HttpDelete("notes/{noteId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteUserNote(
        string channelId,
        int noteId,
        CancellationToken ct
    )
    {
        Result result = await _moderationService.DeleteUserNoteAsync(channelId, noteId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    // ─── Viewer reports ───────────────────────────────────────────────────────

    /// <summary>File a report about a chatter (any viewer in the channel). Enters the moderator triage queue.</summary>
    [RequireAction("moderation:report:file")]
    [HttpPost("reports")]
    [ProducesResponseType<StatusResponseDto<ViewerReportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FileReport(
        string channelId,
        [FromBody] FileViewerReportRequest request,
        CancellationToken ct
    )
    {
        string actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        Result<ViewerReportDto> result = await _reports.FileReportAsync(
            channelId,
            request,
            actorId,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<ViewerReportDto> { Data = result.Value });
    }

    /// <summary>List the channel's viewer reports (default: the open queue).</summary>
    [RequireAction("moderation:report:read")]
    [HttpGet("reports")]
    [ProducesResponseType<StatusResponseDto<List<ViewerReportDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListReports(
        string channelId,
        [FromQuery] string status,
        CancellationToken ct
    )
    {
        Result<List<ViewerReportDto>> result = await _reports.ListReportsAsync(
            channelId,
            string.IsNullOrWhiteSpace(status) ? "open" : status,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Resolve a viewer report — <c>dismiss</c> or <c>escalate</c> — recording the acting moderator.</summary>
    [RequireAction("moderation:report:triage")]
    [HttpPatch("reports/{reportId:guid}")]
    [ProducesResponseType<StatusResponseDto<ViewerReportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveReport(
        string channelId,
        Guid reportId,
        [FromBody] ResolveViewerReportRequest request,
        CancellationToken ct
    )
    {
        string actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        Result<ViewerReportDto> result = await _reports.ResolveReportAsync(
            channelId,
            reportId,
            request.Action,
            actorId,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<ViewerReportDto> { Data = result.Value });
    }

    // ─── Stats ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Get today's moderation counts for the channel — bans, timeouts, deleted messages, and AutoMod actions —
    /// derived from the channel's event log.
    /// </summary>
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

    /// <summary>List moderation actions (bans, timeouts, unbans) taken in the channel, paginated.</summary>
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

    /// <summary>Time out, ban, or unban a user in the channel, dispatched by the requested action type.</summary>
    [RequireAction("moderation:ban")]
    [HttpPost("actions")]
    [ProducesResponseType<StatusResponseDto<ModerationActionResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PerformAction(
        string channelId,
        [FromBody] PerformModerationActionRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        string actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        Result<ModerationActionResult> result = request.Action switch
        {
            "timeout" => await _moderationService.TimeoutAsync(
                channelId,
                operatorUserId,
                request.TargetUserId,
                request.DurationSeconds ?? 600,
                request.Reason,
                actorId,
                ct
            ),

            "ban" => await _moderationService.BanAsync(
                channelId,
                operatorUserId,
                request.TargetUserId,
                request.Reason,
                actorId,
                ct
            ),

            "unban" => await _moderationService.UnbanAsync(
                channelId,
                operatorUserId,
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

    /// <summary>Send a Twitch shoutout for another channel from the broadcaster's channel.</summary>
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
}
