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
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/stream")]
[Authorize]
[Tags("Stream")]
public class StreamController : BaseController
{
    private readonly IChannelService _channelService;
    private readonly IChannelRegistry _registry;
    private readonly ITwitchApiService _twitchApi;
    private readonly ITwitchIdentityResolver _identityResolver;
    private readonly IApplicationDbContext _db;

    public StreamController(
        IChannelService channelService,
        IChannelRegistry registry,
        ITwitchApiService twitchApi,
        ITwitchIdentityResolver identityResolver,
        IApplicationDbContext db
    )
    {
        _channelService = channelService;
        _registry = registry;
        _twitchApi = twitchApi;
        _identityResolver = identityResolver;
        _db = db;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record StreamInfoDto(
        string? Title,
        string? GameName,
        List<string> Tags,
        bool IsLive,
        int ViewerCount,
        DateTime? StartedAt,
        string? Language,
        DateTime? LastStreamedAt = null
    );

    public record UpdateStreamRequest(string? Title, string? GameName, List<string>? Tags);

    public record UpdateTitleRequest(string Title);

    public record UpdateGameRequest(string GameName);

    public record UpdateTagsRequest(List<string> Tags);

    public record StreamStatusDto(bool IsLive, int ViewerCount);

    public record CategoryDto(string Id, string Name, string? BoxArtUrl);

    // ── Get current stream info ──────────────────────────────────────────────

    [HttpGet]
    [ProducesResponseType<StatusResponseDto<StreamInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStreamInfo(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        // Helix calls take the Twitch channel string id, resolved from the tenant Guid.
        string? twitchChannelId = await _identityResolver.GetTwitchChannelIdAsync(tenantId, ct);

        ChannelContext? ctx = _registry.Get(tenantId);

        if (ctx is not null)
        {
            // Enrich with live viewer count and channel info (tags, language)
            int viewerCount = 0;
            List<string> tags = [];
            string? language = null;

            if (ctx.IsLive && twitchChannelId is not null)
            {
                TwitchStreamInfo? streamInfo = await _twitchApi.GetStreamInfoAsync(
                    twitchChannelId,
                    ct
                );
                viewerCount = streamInfo?.ViewerCount ?? 0;
            }

            TwitchChannelInfo? channelInfo = twitchChannelId is null
                ? null
                : await _twitchApi.GetChannelInfoAsync(twitchChannelId, ct);
            if (channelInfo is not null)
            {
                tags = channelInfo.Tags;
                language = channelInfo.Language;
            }

            DateTime? lastStreamedAt = null;
            if (!ctx.IsLive)
            {
                lastStreamedAt = await _db
                    .Streams.Where(s => s.ChannelId == tenantId && s.EndedAt != null)
                    .OrderByDescending(s => s.EndedAt)
                    .Select(s => (DateTime?)s.EndedAt!.Value.UtcDateTime)
                    .FirstOrDefaultAsync(ct);
            }

            var info = new StreamInfoDto(
                ctx.CurrentTitle ?? channelInfo?.Title,
                ctx.CurrentGame ?? channelInfo?.GameName,
                tags,
                ctx.IsLive,
                viewerCount,
                ctx.WentLiveAt?.UtcDateTime,
                language,
                lastStreamedAt
            );

            return Ok(new StatusResponseDto<StreamInfoDto> { Data = info });
        }

        // Channel not in registry — fetch real info from Twitch API
        TwitchChannelInfo? twitchChannel = twitchChannelId is null
            ? null
            : await _twitchApi.GetChannelInfoAsync(twitchChannelId, ct);

        Result<ChannelDto> result = await _channelService.GetAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        ChannelDto channel = result.Value;

        DateTime? lastStreamedAtFallback = channel.IsLive
            ? null
            : await _db
                .Streams.Where(s => s.ChannelId == tenantId && s.EndedAt != null)
                .OrderByDescending(s => s.EndedAt)
                .Select(s => (DateTime?)s.EndedAt!.Value.UtcDateTime)
                .FirstOrDefaultAsync(ct);

        var fallback = new StreamInfoDto(
            twitchChannel?.Title ?? channel.Title,
            twitchChannel?.GameName ?? channel.GameName,
            twitchChannel?.Tags ?? [],
            channel.IsLive,
            channel.ViewerCount ?? 0,
            null,
            twitchChannel?.Language ?? channel.Language,
            lastStreamedAtFallback
        );

        return Ok(new StatusResponseDto<StreamInfoDto> { Data = fallback });
    }

    // ── Update stream info ───────────────────────────────────────────────────

    [HttpPut]
    [ProducesResponseType<StatusResponseDto<StreamInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateStreamInfo(
        string channelId,
        [FromBody] UpdateStreamRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        string? twitchChannelId = await _identityResolver.GetTwitchChannelIdAsync(tenantId, ct);
        if (twitchChannelId is null)
            return NotFoundResponse("Channel not found.");

        Result<ChannelDto> result = await _channelService.GetAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        // Resolve game name → game ID via Twitch search
        string? gameId = null;
        string? resolvedGameName = request.GameName;
        if (request.GameName is not null)
        {
            IReadOnlyList<TwitchCategoryInfo> categories = await _twitchApi.SearchCategoriesAsync(
                request.GameName,
                ct
            );
            TwitchCategoryInfo? match =
                categories.FirstOrDefault(c =>
                    string.Equals(c.Name, request.GameName, StringComparison.OrdinalIgnoreCase)
                ) ?? categories.FirstOrDefault();

            if (match is not null)
            {
                gameId = match.Id;
                resolvedGameName = match.Name;
            }
        }

        // Push changes to Twitch
        await _twitchApi.UpdateChannelInfoAsync(
            twitchChannelId,
            request.Title,
            gameId,
            request.Tags,
            ct
        );

        // Update in-memory context
        ChannelContext? ctx = _registry.Get(tenantId);
        if (ctx is not null)
        {
            if (request.Title is not null)
                ctx.CurrentTitle = request.Title;
            if (resolvedGameName is not null)
                ctx.CurrentGame = resolvedGameName;
        }

        ChannelDto channel = result.Value;
        var info = new StreamInfoDto(
            request.Title ?? channel.Title,
            resolvedGameName ?? channel.GameName,
            request.Tags ?? [],
            channel.IsLive,
            channel.ViewerCount ?? 0,
            null,
            null
        );

        return Ok(new StatusResponseDto<StreamInfoDto> { Data = info });
    }

    // ── Lightweight live status ──────────────────────────────────────────────

    [HttpGet("status")]
    [ProducesResponseType<StatusResponseDto<StreamStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        ChannelContext? ctx = _registry.Get(tenantId);

        if (ctx is not null)
        {
            return Ok(
                new StatusResponseDto<StreamStatusDto> { Data = new StreamStatusDto(ctx.IsLive, 0) }
            );
        }

        Result<ChannelDto> result = await _channelService.GetAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return Ok(
            new StatusResponseDto<StreamStatusDto>
            {
                Data = new StreamStatusDto(result.Value.IsLive, result.Value.ViewerCount ?? 0),
            }
        );
    }

    // ── PATCH sub-routes (used by StreamScreen) ──────────────────────────────

    [HttpPatch("title")]
    [ProducesResponseType<StatusResponseDto<StreamInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTitle(
        string channelId,
        [FromBody] UpdateTitleRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        string? twitchChannelId = await _identityResolver.GetTwitchChannelIdAsync(tenantId, ct);
        if (twitchChannelId is null)
            return NotFoundResponse("Channel not found.");

        await _twitchApi.UpdateChannelInfoAsync(twitchChannelId, request.Title, null, null, ct);

        ChannelContext? ctx = _registry.Get(tenantId);
        if (ctx is not null)
            ctx.CurrentTitle = request.Title;

        return await GetStreamInfo(channelId, ct);
    }

    [HttpPatch("game")]
    [ProducesResponseType<StatusResponseDto<StreamInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateGame(
        string channelId,
        [FromBody] UpdateGameRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        string? twitchChannelId = await _identityResolver.GetTwitchChannelIdAsync(tenantId, ct);
        if (twitchChannelId is null)
            return NotFoundResponse("Channel not found.");

        string? gameId = null;
        string resolvedName = request.GameName;

        IReadOnlyList<TwitchCategoryInfo> results = await _twitchApi.SearchCategoriesAsync(
            request.GameName,
            ct
        );
        TwitchCategoryInfo? match =
            results.FirstOrDefault(c =>
                string.Equals(c.Name, request.GameName, StringComparison.OrdinalIgnoreCase)
            ) ?? results.FirstOrDefault();

        if (match is not null)
        {
            gameId = match.Id;
            resolvedName = match.Name;
        }

        await _twitchApi.UpdateChannelInfoAsync(twitchChannelId, null, gameId, null, ct);

        ChannelContext? ctx = _registry.Get(tenantId);
        if (ctx is not null)
            ctx.CurrentGame = resolvedName;

        return await GetStreamInfo(channelId, ct);
    }

    [HttpPatch("tags")]
    [ProducesResponseType<StatusResponseDto<StreamInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTags(
        string channelId,
        [FromBody] UpdateTagsRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        string? twitchChannelId = await _identityResolver.GetTwitchChannelIdAsync(tenantId, ct);
        if (twitchChannelId is null)
            return NotFoundResponse("Channel not found.");

        await _twitchApi.UpdateChannelInfoAsync(twitchChannelId, null, null, request.Tags, ct);
        return await GetStreamInfo(channelId, ct);
    }

    // ── Category search (autocomplete) ───────────────────────────────────────

    [HttpGet("categories")]
    [ProducesResponseType<StatusResponseDto<List<CategoryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchCategories(
        [FromQuery] string query,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            return Ok(new StatusResponseDto<List<CategoryDto>> { Data = [] });

        IReadOnlyList<TwitchCategoryInfo> results = await _twitchApi.SearchCategoriesAsync(
            query,
            ct
        );
        List<CategoryDto> categories = results
            .Select(c => new CategoryDto(c.Id, c.Name, c.BoxArtUrl))
            .ToList();

        return Ok(new StatusResponseDto<List<CategoryDto>> { Data = categories });
    }
}
