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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

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
    private readonly IChannelAccessService _channelAccess;
    private readonly IMembershipService _memberships;
    private readonly IUserService _users;

    public ChannelsController(
        IChannelService channelService,
        IApplicationDbContext db,
        ITwitchModeratorsApi moderators,
        IChannelAccessService channelAccess,
        IMembershipService memberships,
        IUserService users
    )
    {
        _channelService = channelService;
        _db = db;
        _moderators = moderators;
        _channelAccess = channelAccess;
        _memberships = memberships;
        _users = users;
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

        // Fetch channels the caller moderates on Twitch so they appear even if not yet synced to the
        // ChannelModerators table. Resolve the caller's OWN channel from their identity — the key the
        // moderators sub-client turns into the caller's Twitch user id (via Channel.User.TwitchUserId) and
        // its integration token. (The old read of a `broadcaster_id` claim always returned null — the JWT
        // carries a `tenant` claim, never `broadcaster_id` — so this enrichment silently never ran.)
        IReadOnlyList<string> moderatedIds = [];
        Guid ownChannel = await _channelAccess.ResolveOwnChannelAsync(userId, ct);
        if (ownChannel != Guid.Empty)
        {
            Result<TwitchPage<TwitchModeratedChannel>> moderated =
                await _moderators.GetModeratedChannelsAsync(
                    ownChannel,
                    new TwitchPageRequest(),
                    ct
                );
            if (moderated.IsSuccess)
                moderatedIds = [.. moderated.Value.Items.Select(m => m.BroadcasterId)];
        }

        // Twitch's own role rules are the out-of-box baseline (roles-permissions §0): a caller who moderates an
        // onboarded channel gets the Moderator management role there. The per-channel onboarding sync can't grant
        // this when the CHANNEL's own token is dead, so grant it lazily here from the caller's WORKING token — the
        // moderated list above was resolved with it. Without this, a Twitch mod resolves as a role-less viewer on
        // channels they moderate and is dropped onto the participant surface with no mod tools.
        if (Guid.TryParse(userId, out Guid callerGuid))
            await EnsureModeratorMembershipsAsync(callerGuid, moderatedIds, ct);

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

    // A caller who moderates an onboarded channel on Twitch should hold the Moderator management role there
    // (roles-permissions §0 — Twitch's own role rules are the baseline). Idempotently grants a TwitchBadge
    // Moderator membership only where the caller has NONE, so an existing / higher role is never downgraded.
    private async Task EnsureModeratorMembershipsAsync(
        Guid userGuid,
        IReadOnlyList<string> moderatedTwitchChannelIds,
        CancellationToken ct
    )
    {
        if (moderatedTwitchChannelIds.Count == 0)
            return;

        List<Guid> onboardedModerated = await _db
            .Channels.Where(c =>
                moderatedTwitchChannelIds.Contains(c.TwitchChannelId) && c.IsOnboarded
            )
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (onboardedModerated.Count == 0)
            return;

        // IgnoreQueryFilters: onboardedModerated are channels the caller MODERATES — every one is a tenant other
        // than the request's resolved tenant (the caller's own channel). The global tenant filter would scope this
        // to the resolved tenant and thus never see those memberships, so the grant below would try to re-insert a
        // row that already exists (a hard 23505 against the partial unique index). Soft-delete is still applied by
        // the explicit DeletedAt == null.
        HashSet<Guid> alreadyMember = await _db
            .ChannelMemberships.IgnoreQueryFilters()
            .Where(m =>
                m.UserId == userGuid
                && onboardedModerated.Contains(m.BroadcasterId)
                && m.DeletedAt == null
            )
            .Select(m => m.BroadcasterId)
            .ToHashSetAsync(ct);

        foreach (Guid channelId in onboardedModerated)
        {
            if (alreadyMember.Contains(channelId))
                continue;
            // grantedByUserId null → a system sync (not a delegated grant), so the no-escalation guard is skipped.
            await _memberships.SetManagementRoleAsync(
                channelId,
                userGuid,
                ManagementRole.Moderator,
                MembershipSource.TwitchBadge,
                grantedByUserId: null,
                ct
            );
        }
    }

    /// <summary>Get all Twitch channels the current user moderates (from Twitch API, not just DB).</summary>
    [HttpGet("moderated")]
    [ProducesResponseType<StatusResponseDto<List<ModeratedChannelDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetModeratedChannels(CancellationToken ct)
    {
        string? userId =
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return UnauthenticatedResponse();

        // "Channels I moderate" is a property of the caller. Resolve the caller's OWN channel (the key the
        // moderators sub-client turns into the caller's Twitch user id) from their identity — the old code read
        // a `broadcaster_id` claim that is never minted (the JWT carries `tenant`), so this always 401'd. A
        // caller with no owned channel yet simply moderates nothing here.
        Guid moderatorChannelId = await _channelAccess.ResolveOwnChannelAsync(userId, ct);
        if (moderatorChannelId == Guid.Empty)
            return Ok(new StatusResponseDto<List<ModeratedChannelDto>> { Data = [] });

        Result<TwitchPage<TwitchModeratedChannel>> moderatedResult =
            await _moderators.GetModeratedChannelsAsync(
                moderatorChannelId,
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
            .Channels.Where(c => allIds.Contains(c.TwitchChannelId!) && c.IsOnboarded)
            .Select(c => c.TwitchChannelId!)
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

    /// <summary>
    /// Enter a Twitch channel the caller moderates but the bot is NOT installed on ("Moderator mode"). Provisions
    /// a lightweight, un-onboarded tenant for the broadcaster and grants the caller the Moderator management role,
    /// so tenant resolution (Gate 1) admits them and the dashboard can switch to the channel. Gated at
    /// <c>dashboard:read</c>, but the real authorization is the live Twitch moderation check below — the caller
    /// must actually moderate <paramref name="twitchBroadcasterId"/>.
    /// </summary>
    [HttpPost("moderated/{twitchBroadcasterId}/enter")]
    [RequireAction("dashboard:read")]
    [ProducesResponseType<StatusResponseDto<ChannelSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EnterModeratedChannel(
        string twitchBroadcasterId,
        CancellationToken ct
    )
    {
        string? userId =
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out Guid callerGuid))
            return UnauthenticatedResponse();

        // Authorize against Twitch's own role rules: the caller must currently moderate this channel. Resolve
        // the moderated list from the caller's OWN channel + working token (the key the moderators sub-client
        // turns into the caller's Twitch user id), exactly as GetModeratedChannels does.
        Guid moderatorChannelId = await _channelAccess.ResolveOwnChannelAsync(userId, ct);
        if (moderatorChannelId == Guid.Empty)
            return UnauthorizedResponse("You do not moderate this channel.");

        Result<TwitchPage<TwitchModeratedChannel>> moderatedResult =
            await _moderators.GetModeratedChannelsAsync(
                moderatorChannelId,
                new TwitchPageRequest(),
                ct
            );
        TwitchModeratedChannel? match = moderatedResult.IsSuccess
            ? moderatedResult.Value.Items.FirstOrDefault(m =>
                m.BroadcasterId == twitchBroadcasterId
            )
            : null;
        if (match is null)
            return UnauthorizedResponse("You do not moderate this channel.");

        string login = match.BroadcasterLogin;
        string displayName = match.BroadcasterName;

        // Get-or-create the broadcaster as a User (the owner of the tenant we provision) — a viewer/non-setup
        // identity is fine; they haven't installed the bot.
        Result<UserDto> ownerResult = await _users.GetOrCreateAsync(
            twitchBroadcasterId,
            login,
            displayName,
            cancellationToken: ct
        );
        if (ownerResult.IsFailure)
            return ResultResponse(ownerResult);
        if (!Guid.TryParse(ownerResult.Value.Id, out Guid ownerUserId))
            return InternalServerErrorResponse("Could not resolve broadcaster user id.");

        Result<Guid> tenant = await _channelService.EnsureModeratedTenantAsync(
            twitchBroadcasterId,
            login,
            displayName,
            ownerUserId,
            ct
        );
        if (tenant.IsFailure)
            return ResultResponse(tenant);

        // Grant the caller the Moderator management role on the new tenant so Gate 2 resolves them correctly.
        // Idempotent; grantedByUserId null → a system sync (no-escalation guard skipped), mirroring
        // EnsureModeratorMembershipsAsync above.
        await _memberships.SetManagementRoleAsync(
            tenant.Value,
            callerGuid,
            ManagementRole.Moderator,
            MembershipSource.TwitchBadge,
            grantedByUserId: null,
            ct
        );

        ChannelSummaryDto summary = new(
            tenant.Value.ToString(),
            login,
            displayName,
            null,
            false,
            "moderator",
            null,
            null
        );
        return Ok(new StatusResponseDto<ChannelSummaryDto> { Data = summary });
    }

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

    /// <summary>
    /// Get the channel's built-in-command personality tone. Moderator-floored like every dashboard read.
    /// </summary>
    [HttpGet("{channelId}/settings/personality")]
    [RequireAction("dashboard:read")]
    [ProducesResponseType<StatusResponseDto<ChannelPersonalityDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPersonality(string channelId, CancellationToken ct)
    {
        Result<ChannelPersonalityDto> result = await _channelService.GetPersonalityAsync(
            channelId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Set the channel's built-in-command personality tone (Informative, Friendly, Sassy, Hype, Chill).
    /// Broadcaster/Editor-gated like the other channel settings writes.
    /// </summary>
    [HttpPut("{channelId}/settings/personality")]
    [RequireAction("setup:write")]
    [ProducesResponseType<StatusResponseDto<ChannelPersonalityDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetPersonality(
        string channelId,
        [FromBody] SetChannelPersonalityRequest request,
        CancellationToken ct
    )
    {
        Result<ChannelPersonalityDto> result = await _channelService.SetPersonalityAsync(
            channelId,
            request.Personality,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<ChannelPersonalityDto> { Data = result.Value });
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
