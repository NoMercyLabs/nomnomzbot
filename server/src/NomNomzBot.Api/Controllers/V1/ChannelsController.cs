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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels")]
[Authorize]
[Tags("Channels")]
public class ChannelsController : BaseController
{
    private readonly IChannelService _channelService;
    private readonly IApplicationDbContext _db;
    private readonly ITwitchApiService _twitchApi;
    private readonly IRewardService _rewardService;
    private readonly ILogger<ChannelsController> _logger;
    private readonly TimeProvider _timeProvider;

    public ChannelsController(
        IChannelService channelService,
        IApplicationDbContext db,
        ITwitchApiService twitchApi,
        IRewardService rewardService,
        ILogger<ChannelsController> logger,
        TimeProvider timeProvider
    )
    {
        _channelService = channelService;
        _db = db;
        _twitchApi = twitchApi;
        _rewardService = rewardService;
        _logger = logger;
        _timeProvider = timeProvider;
    }

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

        // Fetch channels the user moderates on Twitch so they appear even if not yet
        // synced to the ChannelModerators table.
        IReadOnlyList<string> moderatedIds = [];
        try
        {
            IReadOnlyList<TwitchModeratedChannel> moderated =
                await _twitchApi.GetModeratedChannelsAsync(userId, ct);
            moderatedIds = moderated.Select(m => m.BroadcasterId).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch moderated channels from Twitch for user {UserId}",
                userId
            );
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
        string? userId =
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
            return UnauthenticatedResponse();

        IReadOnlyList<TwitchModeratedChannel> moderated =
            await _twitchApi.GetModeratedChannelsAsync(userId, ct);

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

    [HttpGet("{channelId}")]
    [ProducesResponseType<StatusResponseDto<ChannelDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChannel(string channelId, CancellationToken ct)
    {
        Result<ChannelDto> result = await _channelService.GetAsync(channelId, ct);
        return ResultResponse(result);
    }

    [HttpPost]
    [ProducesResponseType<StatusResponseDto<ChannelDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> OnboardChannel(
        [FromBody] CreateChannelRequest request,
        CancellationToken ct
    )
    {
        Result<ChannelDto> result = await _channelService.OnboardAsync(
            request.BroadcasterId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);

        // result.Value.Id is the tenant (channel) Guid as a string; request.BroadcasterId is the
        // Twitch broadcaster string id. Service calls take the tenant string; Helix takes the Twitch id.
        string channelId = result.Value.Id;
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return InternalServerErrorResponse("Onboarded channel returned an invalid id.");
        string twitchChannelId = request.BroadcasterId;

        // Link any pre-existing broadcaster token (stored with BroadcasterId=null during login)
        Service? unlinkedToken = await _db.Services.FirstOrDefaultAsync(
            s => s.Name == "twitch" && s.BroadcasterId == null && s.UserId == request.BroadcasterId,
            ct
        );
        if (unlinkedToken is not null)
        {
            unlinkedToken.BroadcasterId = tenantId;
            await _db.SaveChangesAsync(ct);
        }

        // Auto-mod the platform bot in the new channel
        Service? botService = await _db.Services.FirstOrDefaultAsync(
            s => s.Name == "twitch_bot" && s.BroadcasterId == null && s.UserId != null,
            ct
        );
        if (botService?.UserId is not null)
        {
            await _twitchApi.AddModeratorAsync(twitchChannelId, botService.UserId, ct);
        }

        // ── Full Twitch data sync on onboarding ─────────────────────────────
        // Each step is independent — one failure must not block the rest.
        try
        {
            TwitchChannelInfo? channelInfo = await _twitchApi.GetChannelInfoAsync(
                twitchChannelId,
                ct
            );
            if (channelInfo is not null)
            {
                Channel? channel = await _db.Channels.FindAsync([tenantId], ct);
                if (channel is not null)
                {
                    channel.Title = channelInfo.Title;
                    channel.GameName = channelInfo.GameName;
                    channel.GameId = channelInfo.GameId;
                    channel.Tags = channelInfo.Tags;
                    channel.Language = channelInfo.Language;
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "Synced channel info for {ChannelId}: {Title} / {Game}",
                        channelId,
                        channelInfo.Title,
                        channelInfo.GameName
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync channel info for {ChannelId}", channelId);
        }

        try
        {
            await _rewardService.SyncWithTwitchAsync(channelId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync rewards for {ChannelId}", channelId);
        }

        try
        {
            IReadOnlyList<TwitchBannedUser> bannedUsers = await _twitchApi.GetBannedUsersAsync(
                twitchChannelId,
                ct
            );
            foreach (TwitchBannedUser ban in bannedUsers)
            {
                bool exists = await _db.Configurations.AnyAsync(
                    c => c.BroadcasterId == tenantId && c.Key == $"ban:{ban.UserId}",
                    ct
                );
                if (!exists)
                {
                    _db.Configurations.Add(
                        new NomNomzBot.Domain.Platform.Entities.Configuration
                        {
                            BroadcasterId = tenantId,
                            Key = $"ban:{ban.UserId}",
                            Value = System.Text.Json.JsonSerializer.Serialize(
                                new
                                {
                                    userId = ban.UserId,
                                    username = ban.UserLogin,
                                    displayName = ban.UserName ?? ban.UserLogin,
                                    reason = ban.Reason,
                                    bannedBy = "",
                                    bannedAt = _timeProvider.GetUtcNow().UtcDateTime.ToString("o"),
                                }
                            ),
                        }
                    );
                }
            }
            if (bannedUsers.Count > 0)
                await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Synced {Count} banned users for {ChannelId}",
                bannedUsers.Count,
                channelId
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync banned users for {ChannelId}", channelId);
        }

        // ── Seed default commands for the new channel ─────────────────────────
        try
        {
            (
                string Name,
                string PipelineJson,
                string Permission,
                int CooldownSeconds,
                string Description
            )[] defaultCommands = new (
                string Name,
                string PipelineJson,
                string Permission,
                int CooldownSeconds,
                string Description
            )[]
            {
                (
                    "!sr",
                    """{"steps":[{"action":{"type":"music_request"}}]}""",
                    "everyone",
                    5,
                    "Request a song"
                ),
                (
                    "!skip",
                    """{"steps":[{"action":{"type":"music_skip"}}]}""",
                    "moderator",
                    0,
                    "Skip the current song"
                ),
                (
                    "!queue",
                    """{"steps":[{"action":{"type":"music_queue"}}]}""",
                    "everyone",
                    10,
                    "Show the song queue"
                ),
                (
                    "!volume",
                    """{"steps":[{"action":{"type":"music_volume"}}]}""",
                    "moderator",
                    0,
                    "Set the music volume"
                ),
                (
                    "!song",
                    """{"steps":[{"action":{"type":"music_current"}}]}""",
                    "everyone",
                    5,
                    "Show the current song"
                ),
            };

            foreach (
                (
                    string Name,
                    string PipelineJson,
                    string Permission,
                    int CooldownSeconds,
                    string Description
                ) def in defaultCommands
            )
            {
                bool exists = await _db.Commands.AnyAsync(
                    c => c.BroadcasterId == tenantId && c.Name == def.Name,
                    ct
                );

                if (!exists)
                {
                    _db.Commands.Add(
                        new NomNomzBot.Domain.Commands.Entities.Command
                        {
                            BroadcasterId = tenantId,
                            Name = def.Name,
                            Type = "pipeline",
                            PipelineJson = def.PipelineJson,
                            Permission = def.Permission,
                            CooldownSeconds = def.CooldownSeconds,
                            Description = def.Description,
                            IsEnabled = true,
                        }
                    );
                }
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded default commands for {ChannelId}", channelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed default commands for {ChannelId}", channelId);
        }

        // ── Seed default event responses for the new channel ────────────────
        try
        {
            (string EventType, string Message)[] defaultEventResponses = new (
                string EventType,
                string Message
            )[]
            {
                ("channel.follow", "Welcome {user}! Thanks for the follow!"),
                ("channel.subscribe", "{user} just subscribed! Thank you for the support!"),
                ("channel.subscription.gift", "{user} gifted {amount} sub(s)! How generous!"),
                (
                    "channel.subscription.message",
                    "{user} resubscribed for {months} months! {message}"
                ),
                ("channel.cheer", "{user} cheered {amount} bits! Thank you!"),
                ("channel.raid", "{user} is raiding with {viewers} viewers! Welcome raiders!"),
            };

            foreach ((string EventType, string Message) def in defaultEventResponses)
            {
                bool exists = await _db.EventResponses.AnyAsync(
                    er => er.BroadcasterId == tenantId && er.EventType == def.EventType,
                    ct
                );

                if (!exists)
                {
                    _db.EventResponses.Add(
                        new NomNomzBot.Domain.Commands.Entities.EventResponse
                        {
                            BroadcasterId = tenantId,
                            EventType = def.EventType,
                            IsEnabled = true,
                            ResponseType = "chat_message",
                            Message = def.Message,
                        }
                    );
                }
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded default event responses for {ChannelId}", channelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to seed default event responses for {ChannelId}",
                channelId
            );
        }

        try
        {
            IReadOnlyList<TwitchModeratorInfo> mods = await _twitchApi.GetModeratorsAsync(
                twitchChannelId,
                ct
            );
            IReadOnlyList<TwitchVipInfo> vips = await _twitchApi.GetVipsAsync(twitchChannelId, ct);

            // mod/vip .UserId are Twitch user string ids; Users key on TwitchUserId, the FK target
            // (ChannelModerator.UserId) is the internal User.Id Guid.
            List<string> allTwitchUserIds = mods.Select(m => m.UserId)
                .Concat(vips.Select(v => v.UserId))
                .Distinct()
                .ToList();
            List<string> existingTwitchUserIds = await _db
                .Users.Where(u => allTwitchUserIds.Contains(u.TwitchUserId))
                .Select(u => u.TwitchUserId)
                .ToListAsync(ct);

            foreach (
                TwitchModeratorInfo? mod in mods.Where(m =>
                    !existingTwitchUserIds.Contains(m.UserId)
                )
            )
            {
                _db.Users.Add(
                    new NomNomzBot.Domain.Identity.Entities.User
                    {
                        TwitchUserId = mod.UserId,
                        Username = mod.UserLogin,
                        DisplayName = mod.UserName ?? mod.UserLogin,
                    }
                );
            }
            foreach (
                TwitchVipInfo? vip in vips.Where(v =>
                    !existingTwitchUserIds.Contains(v.UserId) && mods.All(m => m.UserId != v.UserId)
                )
            )
            {
                _db.Users.Add(
                    new NomNomzBot.Domain.Identity.Entities.User
                    {
                        TwitchUserId = vip.UserId,
                        Username = vip.UserLogin,
                        DisplayName = vip.UserName ?? vip.UserLogin,
                    }
                );
            }

            await _db.SaveChangesAsync(ct);

            // Resolve Twitch user ids → internal User.Id Guids for the moderator FK.
            Dictionary<string, Guid> userIdMap = await _db
                .Users.Where(u => allTwitchUserIds.Contains(u.TwitchUserId))
                .ToDictionaryAsync(u => u.TwitchUserId, u => u.Id, ct);

            // Store mod/VIP status in channel moderators table
            foreach (TwitchModeratorInfo mod in mods)
            {
                if (!userIdMap.TryGetValue(mod.UserId, out Guid modUserId))
                    continue;

                bool modExists = await _db.ChannelModerators.AnyAsync(
                    cm => cm.ChannelId == tenantId && cm.UserId == modUserId,
                    ct
                );
                if (!modExists)
                {
                    _db.ChannelModerators.Add(
                        new NomNomzBot.Domain.Identity.Entities.ChannelModerator
                        {
                            ChannelId = tenantId,
                            UserId = modUserId,
                            GrantedAt = _timeProvider.GetUtcNow().UtcDateTime,
                        }
                    );
                }
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Synced {ModCount} mods and {VipCount} VIPs for {ChannelId}",
                mods.Count,
                vips.Count,
                channelId
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync mods/VIPs for {ChannelId}", channelId);
        }

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

    [HttpPut("{channelId}")]
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

    [HttpPost("{channelId}/join")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> JoinChannel(string channelId, CancellationToken ct)
    {
        Result result = await _channelService.JoinAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<object> { Message = "Bot joined channel." });
    }

    [HttpPost("{channelId}/leave")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LeaveChannel(string channelId, CancellationToken ct)
    {
        Result result = await _channelService.LeaveAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<object> { Message = "Bot left channel." });
    }

    [HttpDelete("{channelId}")]
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
