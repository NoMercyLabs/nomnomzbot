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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Aggregated per-channel stats and activity feed for the dashboard home screen.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Authorize]
[Tags("Dashboard")]
public class DashboardController : BaseController
{
    private readonly IChannelRegistry _registry;
    private readonly IChannelService _channelService;
    private readonly IApplicationDbContext _db;
    private readonly ITwitchChannelsApi _channels;
    private readonly ITwitchStreamsApi _streams;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IChannelRegistry registry,
        IChannelService channelService,
        IApplicationDbContext db,
        ITwitchChannelsApi channels,
        ITwitchStreamsApi streams,
        TimeProvider timeProvider,
        ILogger<DashboardController> logger
    )
    {
        _registry = registry;
        _channelService = channelService;
        _db = db;
        _channels = channels;
        _streams = streams;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record ActivityEventDto(
        string Id,
        string Type,
        string? UserId,
        string? Username,
        string? Data,
        DateTime Timestamp
    );

    /// <summary>
    /// Returns a live stats snapshot for the given channel.
    /// Uses the in-memory ChannelContext when the bot is connected; falls back to DB otherwise.
    /// </summary>
    [HttpGet("{channelId}/stats")]
    [RequireAction("dashboard:read")]
    [ProducesResponseType<StatusResponseDto<DashboardStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        // Fetch follower count and channel info sequentially — both calls resolve the Twitch channel
        // ID via TwitchIdentityResolver which uses the scoped DbContext. Running them in parallel via
        // Task.WhenAll causes "A second operation was started on this context instance" because EF Core
        // DbContext is not thread-safe. Sequential execution is safe and still fast (<500 ms each).
        int followerCount = 0;
        string? twitchTitle = null;
        string? twitchGame = null;

        try
        {
            Result<int> followerResult = await _channels.GetChannelFollowerCountAsync(tenantId, ct);
            followerCount = followerResult.IsSuccess ? followerResult.Value : 0;
            if (followerResult.IsFailure)
                _logger.LogWarning(
                    "Dashboard stats: follower count failed for {BroadcasterId}: {Error} ({Code}) — reporting 0",
                    tenantId,
                    followerResult.ErrorMessage,
                    followerResult.ErrorCode
                );

            // Channel info uses app token (no scope required) — always shows the real Twitch title/game.
            Result<TwitchChannelInformation> channelInfoResult =
                await _channels.GetChannelInformationAsync(tenantId, ct);
            twitchTitle = channelInfoResult.IsSuccess ? channelInfoResult.Value.Title : null;
            twitchGame = channelInfoResult.IsSuccess ? channelInfoResult.Value.GameName : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Dashboard stats: Helix calls failed for {BroadcasterId} — returning degraded stats",
                tenantId
            );
        }

        ChannelContext? ctx = _registry.Get(tenantId);

        if (ctx is not null)
        {
            long? uptime =
                ctx.IsLive && ctx.WentLiveAt.HasValue
                    ? (long)(_timeProvider.GetUtcNow() - ctx.WentLiveAt.Value).TotalSeconds
                    : null;

            // Get live viewer count from Twitch stream info (offline ⇒ not_found ⇒ 0).
            int viewerCount = 0;
            if (ctx.IsLive)
            {
                Result<TwitchStream> streamResult = await _streams.GetStreamAsync(tenantId, ct);
                if (streamResult.IsSuccess)
                    viewerCount = streamResult.Value.ViewerCount;
            }

            DashboardStatsDto stats = new()
            {
                IsLive = ctx.IsLive,
                StreamTitle = twitchTitle ?? ctx.CurrentTitle,
                GameName = twitchGame ?? ctx.CurrentGame,
                ViewerCount = viewerCount,
                FollowerCount = followerCount,
                CommandsUsed = ctx.CommandsUsed,
                MessagesCount = ctx.MessageCount,
                Uptime = uptime,
            };

            return Ok(new StatusResponseDto<DashboardStatsDto> { Data = stats });
        }

        // Channel not currently active in registry — fall back to DB for basic info.
        Result<ChannelDto> result = await _channelService.GetAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        ChannelDto channel = result.Value;
        DashboardStatsDto fallback = new()
        {
            IsLive = channel.IsLive,
            StreamTitle = twitchTitle ?? channel.Title,
            GameName = twitchGame ?? channel.GameName,
            ViewerCount = channel.ViewerCount ?? 0,
            FollowerCount = followerCount,
            CommandsUsed = 0,
            MessagesCount = 0,
            Uptime = null,
        };

        return Ok(new StatusResponseDto<DashboardStatsDto> { Data = fallback });
    }

    /// <summary>
    /// Returns recent channel activity events.
    /// </summary>
    [HttpGet("{channelId}/activity")]
    [RequireAction("dashboard:read")]
    [ProducesResponseType<StatusResponseDto<List<ActivityEventDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivity(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        // Chat messages (channel.chat.message) are excluded — they live in the Chat page.
        // Activity shows stream milestones only: follows, subs, raids, cheers, redemptions, etc.
        List<ChannelEvent> events = await _db
            .ChannelEvents.Where(e => e.ChannelId == tenantId && e.Type != "channel.chat.message")
            .OrderByDescending(e => e.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        List<Guid> userIds = events
            .Where(e => e.UserId is not null)
            .Select(e => e.UserId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, User> users = await _db
            .Users.Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        List<ActivityEventDto> result = events
            .Select(e =>
            {
                string? username = null;
                if (e.UserId is not null && users.TryGetValue(e.UserId.Value, out User? user))
                    username = user.DisplayName;

                // Normalize legacy event types imported from the previous bot to their canonical
                // EventSub equivalents so the frontend only needs one switch on the modern names.
                string normalizedType = e.Type switch
                {
                    "raid" => "channel.raid",
                    "follow" => "channel.follow",
                    "subscribe" => "channel.subscribe",
                    "cheer" => "channel.cheer",
                    _ => e.Type,
                };

                return new ActivityEventDto(
                    e.Id,
                    normalizedType,
                    e.UserId?.ToString(),
                    username,
                    e.Data,
                    e.CreatedAt
                );
            })
            .ToList();

        return Ok(new StatusResponseDto<List<ActivityEventDto>> { Data = result });
    }
}
