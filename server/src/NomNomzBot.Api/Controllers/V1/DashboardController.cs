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
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Authorize]
[Tags("Dashboard")]
public class DashboardController : BaseController
{
    private readonly IChannelRegistry _registry;
    private readonly IChannelService _channelService;
    private readonly IApplicationDbContext _db;
    private readonly ITwitchApiService _twitchApi;
    private readonly TimeProvider _timeProvider;

    public DashboardController(
        IChannelRegistry registry,
        IChannelService channelService,
        IApplicationDbContext db,
        ITwitchApiService twitchApi,
        TimeProvider timeProvider
    )
    {
        _registry = registry;
        _channelService = channelService;
        _db = db;
        _twitchApi = twitchApi;
        _timeProvider = timeProvider;
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
    [HttpGet("{broadcasterId}/stats")]
    [ProducesResponseType<StatusResponseDto<DashboardStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(string broadcasterId, CancellationToken ct)
    {
        // Fetch real follower count from Twitch API (fire-and-forget safe — returns 0 on failure)
        int followerCount = await _twitchApi.GetFollowerCountAsync(broadcasterId, ct);

        ChannelContext? ctx = _registry.Get(broadcasterId);

        if (ctx is not null)
        {
            long? uptime =
                ctx.IsLive && ctx.WentLiveAt.HasValue
                    ? (long)(_timeProvider.GetUtcNow() - ctx.WentLiveAt.Value).TotalSeconds
                    : null;

            // Get live viewer count from Twitch stream info
            int viewerCount = 0;
            if (ctx.IsLive)
            {
                TwitchStreamInfo? streamInfo = await _twitchApi.GetStreamInfoAsync(
                    broadcasterId,
                    ct
                );
                viewerCount = streamInfo?.ViewerCount ?? 0;
            }

            DashboardStatsDto stats = new()
            {
                IsLive = ctx.IsLive,
                StreamTitle = ctx.CurrentTitle,
                GameName = ctx.CurrentGame,
                ViewerCount = viewerCount,
                FollowerCount = followerCount,
                CommandsUsed = ctx.CommandsUsed,
                MessagesCount = ctx.MessageCount,
                Uptime = uptime,
            };

            return Ok(new StatusResponseDto<DashboardStatsDto> { Data = stats });
        }

        // Channel not currently active in registry — fall back to DB for basic info.
        Result<ChannelDto> result = await _channelService.GetAsync(broadcasterId, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        ChannelDto channel = result.Value;
        DashboardStatsDto fallback = new()
        {
            IsLive = channel.IsLive,
            StreamTitle = channel.Title,
            GameName = channel.GameName,
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
    [HttpGet("{broadcasterId}/activity")]
    [ProducesResponseType<StatusResponseDto<List<ActivityEventDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivity(string broadcasterId, CancellationToken ct)
    {
        var events = await _db
            .ChannelEvents.Where(e => e.ChannelId == broadcasterId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        var userIds = events
            .Where(e => e.UserId is not null)
            .Select(e => e.UserId!)
            .Distinct()
            .ToList();

        var users = await _db
            .Users.Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var result = events
            .Select(e =>
            {
                string? username = null;
                if (e.UserId is not null && users.TryGetValue(e.UserId, out var user))
                    username = user.DisplayName;

                return new ActivityEventDto(e.Id, e.Type, e.UserId, username, e.Data, e.CreatedAt);
            })
            .ToList();

        return Ok(new StatusResponseDto<List<ActivityEventDto>> { Data = result });
    }
}
