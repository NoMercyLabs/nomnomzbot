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
    private readonly ITwitchChannelsApi _channels;
    private readonly ITwitchStreamsApi _streams;
    private readonly ITwitchSearchApi _search;
    private readonly IApplicationDbContext _db;

    public StreamController(
        IChannelService channelService,
        IChannelRegistry registry,
        ITwitchChannelsApi channels,
        ITwitchStreamsApi streams,
        ITwitchSearchApi search,
        IApplicationDbContext db
    )
    {
        _channelService = channelService;
        _registry = registry;
        _channels = channels;
        _streams = streams;
        _search = search;
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

        ChannelContext? ctx = _registry.Get(tenantId);

        if (ctx is not null)
        {
            // Enrich with live viewer count and channel info (tags, language). The sub-clients resolve the
            // tenant Guid → Twitch id internally; offline / missing-scope degrades to the unenriched values.
            int viewerCount = 0;
            List<string> tags = [];
            string? language = null;

            if (ctx.IsLive)
            {
                Result<TwitchStream> streamResult = await _streams.GetStreamAsync(tenantId, ct);
                if (streamResult.IsSuccess)
                    viewerCount = streamResult.Value.ViewerCount;
            }

            Result<TwitchChannelInformation> channelResult =
                await _channels.GetChannelInformationAsync(tenantId, ct);
            TwitchChannelInformation? channelInfo = channelResult.IsSuccess
                ? channelResult.Value
                : null;
            if (channelInfo is not null)
            {
                tags = [.. channelInfo.Tags];
                language = channelInfo.BroadcasterLanguage;
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

            StreamInfoDto info = new StreamInfoDto(
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

        // Channel not in registry — fetch real info from Twitch.
        Result<TwitchChannelInformation> twitchChannelResult =
            await _channels.GetChannelInformationAsync(tenantId, ct);
        TwitchChannelInformation? twitchChannel = twitchChannelResult.IsSuccess
            ? twitchChannelResult.Value
            : null;

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

        StreamInfoDto fallback = new StreamInfoDto(
            twitchChannel?.Title ?? channel.Title,
            twitchChannel?.GameName ?? channel.GameName,
            twitchChannel is not null ? [.. twitchChannel.Tags] : [],
            channel.IsLive,
            channel.ViewerCount ?? 0,
            null,
            twitchChannel?.BroadcasterLanguage ?? channel.Language,
            lastStreamedAtFallback
        );

        return Ok(new StatusResponseDto<StreamInfoDto> { Data = fallback });
    }

    // ── Update stream info ───────────────────────────────────────────────────

    /// <summary>
    /// Update the channel's stream metadata (title, game, tags) in a single call. Carries the same Editor
    /// floor as the granular PATCH routes (<c>channel:title:write</c>) so a Moderator cannot use this combined
    /// route to bypass the per-field <c>channel:*:write</c> gates (stream-admin §5; all three fields floor at
    /// Editor).
    /// </summary>
    [HttpPut]
    [RequireAction("channel:title:write")]
    [ProducesResponseType<StatusResponseDto<StreamInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateStreamInfo(
        string channelId,
        [FromBody] UpdateStreamRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        Result<ChannelDto> result = await _channelService.GetAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        // Resolve game name → game ID via Twitch search.
        string? gameId = null;
        string? resolvedGameName = request.GameName;
        if (request.GameName is not null)
        {
            Result<TwitchPage<TwitchSearchCategory>> search = await _search.SearchCategoriesAsync(
                request.GameName,
                new TwitchPageRequest(),
                ct
            );
            IReadOnlyList<TwitchSearchCategory> categories = search.IsSuccess
                ? search.Value.Items
                : [];
            TwitchSearchCategory? match =
                categories.FirstOrDefault(c =>
                    string.Equals(c.Name, request.GameName, StringComparison.OrdinalIgnoreCase)
                ) ?? categories.FirstOrDefault();

            if (match is not null)
            {
                gameId = match.Id;
                resolvedGameName = match.Name;
            }
        }

        // Push changes to Twitch; surface a Helix failure (e.g. missing scope) rather than swallowing it.
        Result update = await _channels.ModifyChannelInformationAsync(
            tenantId,
            new ModifyChannelInformationRequest(
                Title: request.Title,
                GameId: gameId,
                Tags: request.Tags
            ),
            ct
        );
        if (update.IsFailure)
            return TwitchResultResponse(update);

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
        StreamInfoDto info = new StreamInfoDto(
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

    [RequireAction("channel:title:write")]
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

        Result update = await _channels.ModifyChannelInformationAsync(
            tenantId,
            new ModifyChannelInformationRequest(Title: request.Title),
            ct
        );
        if (update.IsFailure)
            return TwitchResultResponse(update);

        ChannelContext? ctx = _registry.Get(tenantId);
        if (ctx is not null)
            ctx.CurrentTitle = request.Title;

        return await GetStreamInfo(channelId, ct);
    }

    [RequireAction("channel:game:write")]
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

        string? gameId = null;
        string resolvedName = request.GameName;

        Result<TwitchPage<TwitchSearchCategory>> search = await _search.SearchCategoriesAsync(
            request.GameName,
            new TwitchPageRequest(),
            ct
        );
        IReadOnlyList<TwitchSearchCategory> results = search.IsSuccess ? search.Value.Items : [];
        TwitchSearchCategory? match =
            results.FirstOrDefault(c =>
                string.Equals(c.Name, request.GameName, StringComparison.OrdinalIgnoreCase)
            ) ?? results.FirstOrDefault();

        if (match is not null)
        {
            gameId = match.Id;
            resolvedName = match.Name;
        }

        Result update = await _channels.ModifyChannelInformationAsync(
            tenantId,
            new ModifyChannelInformationRequest(GameId: gameId),
            ct
        );
        if (update.IsFailure)
            return TwitchResultResponse(update);

        ChannelContext? ctx = _registry.Get(tenantId);
        if (ctx is not null)
            ctx.CurrentGame = resolvedName;

        return await GetStreamInfo(channelId, ct);
    }

    [RequireAction("channel:tags:write")]
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

        Result update = await _channels.ModifyChannelInformationAsync(
            tenantId,
            new ModifyChannelInformationRequest(Tags: request.Tags),
            ct
        );
        if (update.IsFailure)
            return TwitchResultResponse(update);

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

        Result<TwitchPage<TwitchSearchCategory>> search = await _search.SearchCategoriesAsync(
            query,
            new TwitchPageRequest(),
            ct
        );
        List<CategoryDto> categories = search.IsSuccess
            ? [.. search.Value.Items.Select(c => new CategoryDto(c.Id, c.Name, c.BoxArtUrl))]
            : [];

        return Ok(new StatusResponseDto<List<CategoryDto>> { Data = categories });
    }
}
