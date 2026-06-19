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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/rewards")]
[Authorize]
[Tags("Rewards")]
public class RewardsController : BaseController
{
    private readonly IRewardService _rewardService;
    private readonly IApplicationDbContext _db;

    public RewardsController(IRewardService rewardService, IApplicationDbContext db)
    {
        _rewardService = rewardService;
        _db = db;
    }

    public record LeaderboardEntryDto(int Rank, string UserId, string DisplayName, int Points);

    [HttpGet]
    [ProducesResponseType<PaginatedResponse<RewardDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRewards(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(
            request.Page,
            request.Take,
            request.Sort,
            request.Order
        );
        Result<PagedList<RewardListItem>> result = await _rewardService.ListAsync(channelId, pagination, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpGet("{rewardId}")]
    [ProducesResponseType<StatusResponseDto<RewardDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReward(
        string channelId,
        string rewardId,
        CancellationToken ct
    )
    {
        Result<RewardDetail> result = await _rewardService.GetAsync(channelId, rewardId, ct);
        return ResultResponse(result);
    }

    [HttpPost]
    [ProducesResponseType<StatusResponseDto<RewardDetail>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateReward(
        string channelId,
        [FromBody] CreateRewardRequest request,
        CancellationToken ct
    )
    {
        Result<RewardDetail> result = await _rewardService.CreateAsync(channelId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetReward),
            new { channelId, rewardId = result.Value.Id },
            new StatusResponseDto<RewardDetail>
            {
                Data = result.Value,
                Message = "Reward created successfully.",
            }
        );
    }

    [HttpPatch("{rewardId}")]
    [ProducesResponseType<StatusResponseDto<RewardDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PatchReward(
        string channelId,
        string rewardId,
        [FromBody] UpdateRewardRequest request,
        CancellationToken ct
    )
    {
        Result<RewardDetail> result = await _rewardService.UpdateAsync(channelId, rewardId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<RewardDetail> { Data = result.Value });
    }

    [HttpPut("{rewardId}")]
    [ProducesResponseType<StatusResponseDto<RewardDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateReward(
        string channelId,
        string rewardId,
        [FromBody] UpdateRewardRequest request,
        CancellationToken ct
    )
    {
        Result<RewardDetail> result = await _rewardService.UpdateAsync(channelId, rewardId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<RewardDetail> { Data = result.Value });
    }

    [HttpDelete("{rewardId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteReward(
        string channelId,
        string rewardId,
        CancellationToken ct
    )
    {
        Result result = await _rewardService.DeleteAsync(channelId, rewardId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    [HttpPost("sync")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncRewards(string channelId, CancellationToken ct)
    {
        Result result = await _rewardService.SyncWithTwitchAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<object> { Message = "Rewards synced with Twitch." });
    }

    [HttpGet("leaderboard")]
    [ProducesResponseType<StatusResponseDto<List<LeaderboardEntryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeaderboard(string channelId, CancellationToken ct)
    {
        var topChatters = await _db.ChatMessages
            .Where(m => m.BroadcasterId == channelId)
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(50)
            .ToListAsync(ct);

        var userIds = topChatters.Select(x => x.UserId).ToList();
        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var entries = topChatters
            .Select((x, i) =>
            {
                users.TryGetValue(x.UserId, out string? displayName);
                return new LeaderboardEntryDto(i + 1, x.UserId, displayName ?? "", x.Count);
            })
            .ToList();

        return Ok(new StatusResponseDto<List<LeaderboardEntryDto>> { Data = entries });
    }
}
