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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Extensions;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages channels and their onboarding, settings, and bot lifecycle.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels")]
[Authorize]
[Tags("Channels")]
public class ChannelsController : BaseController
{
    private readonly IChannelService _channelService;
    private readonly IApplicationDbContext _db;
    private readonly ITwitchModeratorsApi _moderators;

    public ChannelsController(
        IChannelService channelService,
        IApplicationDbContext db,
        ITwitchModeratorsApi moderators
    )
    {
        _channelService = channelService;
        _db = db;
        _moderators = moderators;
    }

    /// <summary>List all channels the current user owns or moderates.</summary>
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<ChannelDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListChannels(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        string? userId =
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
            return UnauthenticatedResponse();

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);

        // Fetch channels the user moderates on Twitch so they appear even if not yet synced to the
        // ChannelModerators table. The broadcaster_id claim is the Channel.Id (Guid) — the key the
        // sub-client uses for both scope lookup (IntegrationConnections.BroadcasterId) and user-id
        // resolution (falls back to Channel.User.TwitchUserId in TwitchIdentityResolver).
        IReadOnlyList<string> moderatedIds = [];
        string? broadcasterIdStr = User.GetBroadcasterId();
        if (Guid.TryParse(broadcasterIdStr, out Guid broadcasterGuid))
        {
            Result<TwitchPage<TwitchModeratedChannel>> moderated =
                await _moderators.GetModeratedChannelsAsync(
                    broadcasterGuid,
                    new TwitchPageRequest(),
                    ct
                );
            if (moderated.IsSuccess)
                moderatedIds = [.. moderated.Value.Items.Select(m => m.BroadcasterId)];
        }

        Result<PagedList<ChannelSummaryDto>> result = await _channelService.GetChannelsAsync(
            userId,
            pagination,
            moderatedIds,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Get all Twitch channels the current user moderates (from Twitch API, not just DB).</summary>
    [HttpGet("moderated")]
    [ProducesResponseType<StatusResponseDto<List<ModeratedChannelDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetModeratedChannels(CancellationToken ct)
    {
        string? broadcasterIdStr = User.GetBroadcasterId();
        if (
            string.IsNullOrEmpty(broadcasterIdStr)
            || !Guid.TryParse(broadcasterIdStr, out Guid moderatorUserId)
        )
            return UnauthenticatedResponse();

        Result<TwitchPage<TwitchModeratedChannel>> moderatedResult =
            await _moderators.GetModeratedChannelsAsync(
                moderatorUserId,
                new TwitchPageRequest(),
                ct
            );
        IReadOnlyList<TwitchModeratedChannel> moderated = moderatedResult.IsSuccess
            ? moderatedResult.Value.Items
            : [];

        // Find which ones are already onboarded in our DB. moderated ids are Twitch channel
        // string ids — match on Channel.TwitchChannelId, not the internal Guid key.
        List<string> allIds = moderated.Select(m => m.BroadcasterId).ToList();
        HashSet<string> onboardedIds = await _db
            .Channels.Where(c => allIds.Contains(c.TwitchChannelId) && c.IsOnboarded)
            .Select(c => c.TwitchChannelId)
            .ToHashSetAsync(ct);

        List<ModeratedChannelDto> dtos = moderated
            .Select(m => new ModeratedChannelDto(
                m.BroadcasterId,
                m.BroadcasterLogin,
                m.BroadcasterName,
                onboardedIds.Contains(m.BroadcasterId)
            ))
            .ToList();

        return Ok(new StatusResponseDto<List<ModeratedChannelDto>> { Data = dtos });
    }

    public record ModeratedChannelDto(
        string Id,
        string Login,
        string DisplayName,
        bool IsOnboarded
    );

    /// <summary>Retrieve a channel by ID — dashboard channel info, Moderator-floored like every dashboard read.</summary>
    [HttpGet("{channelId}")]
    [RequireAction("dashboard:read")]
    [ProducesResponseType<StatusResponseDto<ChannelDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChannel(string channelId, CancellationToken ct)
    {
        Result<ChannelDto> result = await _channelService.GetAsync(channelId, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Onboards the caller's channel. Thin by design: <see cref="IChannelService.OnboardAsync"/> owns every
    /// side effect, including publishing <c>ChannelOnboardedEvent</c> — the single onboarding path that fans
    /// out to the auto-discovered seed handlers (rewards, mods/VIPs/subs, channel info, owner profile, event
    /// responses, banned-user import, bot mod-join, default builtin commands, EventSub subscribe). The former
    /// inline Twitch-data sync that used to live here has moved into those handlers, matching every other
    /// controller's validate → service → response shape.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<ChannelDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> OnboardChannel(
        [FromBody] CreateChannelRequest request,
        CancellationToken ct
    )
    {
        // Self-scoped: the body's BroadcasterId is the OWNER USER id, and onboarding fires the full seed
        // fan-out (EventSub subscribe, bot join, default commands…). Only the caller themselves — or a
        // platform admin — may onboard a channel; a foreign id would let any user onboard/re-onboard
        // someone else's channel.
        string? callerId =
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(callerId))
            return UnauthenticatedResponse();
        if (
            !string.Equals(callerId, request.BroadcasterId, StringComparison.OrdinalIgnoreCase)
            && !User.IsInRole("admin")
        )
            return UnauthorizedResponse("You may only onboard your own channel.");

        Result<ChannelDto> result = await _channelService.OnboardAsync(
            request.BroadcasterId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetChannel),
            new { channelId = result.Value.Id },
            new StatusResponseDto<ChannelDto>
            {
                Data = result.Value,
                Message = "Channel onboarded successfully.",
            }
        );
    }

    /// <summary>Update channel settings (language, timezone, etc.).</summary>
    [HttpPut("{channelId}")]
    [RequireAction("setup:write")]
    [ProducesResponseType<StatusResponseDto<ChannelDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateChannelSettings(
        string channelId,
        [FromBody] UpdateChannelSettingsDto request,
        CancellationToken ct
    )
    {
        Result<ChannelDto> result = await _channelService.UpdateSettingsAsync(
            channelId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<ChannelDto> { Data = result.Value });
    }

    /// <summary>Bot joins the channel (subscribes to EventSub and starts listening).</summary>
    [HttpPost("{channelId}/join")]
    [RequireAction("setup:write")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> JoinChannel(string channelId, CancellationToken ct)
    {
        Result result = await _channelService.JoinAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<object> { Message = "Bot joined channel." });
    }

    /// <summary>Bot leaves the channel (unsubscribes from EventSub).</summary>
    [HttpPost("{channelId}/leave")]
    [RequireAction("setup:write")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LeaveChannel(string channelId, CancellationToken ct)
    {
        Result result = await _channelService.LeaveAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<object> { Message = "Bot left channel." });
    }

    /// <summary>Delete a channel and all its associated data.</summary>
    [HttpDelete("{channelId}")]
    [RequireAction("setup:write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteChannel(string channelId, CancellationToken ct)
    {
        Result result = await _channelService.DeleteAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    /// <summary>Reset all channel bot configuration to defaults (clears Configuration entries).</summary>
    [HttpPost("{channelId}/reset")]
    [RequireAction("setup:write")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetChannel(string channelId, CancellationToken ct)
    {
        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        // Delete all Configuration entries for this channel (settings, TTS, shield, blocked terms, etc.)
        List<NomNomzBot.Domain.Platform.Entities.Configuration> configs = await _db
            .Configurations.Where(c => c.BroadcasterId == broadcasterId)
            .ToListAsync(ct);

        _db.Configurations.RemoveRange(configs);
        await _db.SaveChangesAsync(ct);

        return Ok(
            new StatusResponseDto<object> { Message = "Channel configuration reset to defaults." }
        );
    }
}
