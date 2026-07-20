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
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages stream metadata (title, game, tags, live status).</summary>
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
    private readonly IPlatformChannelApi _platformApi;
    private readonly IApplicationDbContext _db;

    public StreamController(
        IChannelService channelService,
        IChannelRegistry registry,
        ITwitchChannelsApi channels,
        ITwitchStreamsApi streams,
        ITwitchSearchApi search,
        IPlatformChannelApi platformApi,
        IApplicationDbContext db
    )
    {
        _channelService = channelService;
        _registry = registry;
        _channels = channels;
        _streams = streams;
        _search = search;
        _platformApi = platformApi;
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

    /// <summary>One channel that matched a broadcaster-name search (for autocomplete — raid / invite / trust targets).</summary>
    public record ChannelSearchDto(
        string Id,
        string DisplayName,
        string Login,
        string? ThumbnailUrl
    );

    // ── Get current stream info ──────────────────────────────────────────────

    /// <summary>Retrieve current stream information (title, game, tags, live status, viewers).</summary>
    [RequireAction("stream:read")]
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
                // SQLite cannot ORDER BY DateTimeOffset — sort by UUIDv7 Id (time-ordered) instead.
                lastStreamedAt = await _db
                    .Streams.Where(s => s.ChannelId == tenantId && s.EndedAt != null)
                    .OrderByDescending(s => s.Id)
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

        // SQLite cannot ORDER BY a DateTimeOffset; materialize the ended-at values and take the latest
        // client-side so the query is provider-agnostic (Postgres + SQLite).
        List<DateTimeOffset> endedAtValues = channel.IsLive
            ? new List<DateTimeOffset>()
            : await _db
                .Streams.Where(s => s.ChannelId == tenantId && s.EndedAt != null)
                .Select(s => s.EndedAt!.Value)
                .ToListAsync(ct);
        DateTime? lastStreamedAtFallback =
            endedAtValues.Count == 0 ? null : endedAtValues.Max().UtcDateTime;

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

        // Route to the tenant channel's own platform (Twitch resolves the game name to its catalogue
        // spelling; YouTube rejects fields it cannot represent); surface a platform failure (e.g. missing
        // scope) rather than swallowing it.
        Result<PlatformStreamInfoApplied> update = await _platformApi.UpdateStreamInfoAsync(
            tenantId,
            new PlatformStreamInfoUpdate(
                Title: request.Title,
                CategoryName: request.GameName,
                Tags: request.Tags
            ),
            ct
        );
        if (update.IsFailure)
            return TwitchResultResponse(update);
        string? resolvedGameName = update.Value.CategoryName;

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

    /// <summary>Get whether the channel is currently live and current viewer count.</summary>
    [RequireAction("stream:read")]
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

    /// <summary>Update stream title.</summary>
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

        Result<PlatformStreamInfoApplied> update = await _platformApi.UpdateStreamInfoAsync(
            tenantId,
            new PlatformStreamInfoUpdate(Title: request.Title),
            ct
        );
        if (update.IsFailure)
            return TwitchResultResponse(update);

        ChannelContext? ctx = _registry.Get(tenantId);
        if (ctx is not null)
            ctx.CurrentTitle = request.Title;

        return await GetStreamInfo(channelId, ct);
    }

    /// <summary>Update stream game/category (with Twitch search resolution).</summary>
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

        // The platform canonicalizes the category (Twitch resolves the name against its catalogue).
        Result<PlatformStreamInfoApplied> update = await _platformApi.UpdateStreamInfoAsync(
            tenantId,
            new PlatformStreamInfoUpdate(CategoryName: request.GameName),
            ct
        );
        if (update.IsFailure)
            return TwitchResultResponse(update);

        ChannelContext? ctx = _registry.Get(tenantId);
        if (ctx is not null)
            ctx.CurrentGame = update.Value.CategoryName ?? request.GameName;

        return await GetStreamInfo(channelId, ct);
    }

    /// <summary>Update stream tags/categories.</summary>
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

        Result<PlatformStreamInfoApplied> update = await _platformApi.UpdateStreamInfoAsync(
            tenantId,
            new PlatformStreamInfoUpdate(Tags: request.Tags),
            ct
        );
        if (update.IsFailure)
            return TwitchResultResponse(update);

        return await GetStreamInfo(channelId, ct);
    }

    // ── Category search (autocomplete) ───────────────────────────────────────

    /// <summary>Search Twitch game categories (for autocomplete).</summary>
    [RequireAction("stream:read")]
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

    /// <summary>Search Twitch channels/broadcasters by name (for autocomplete — raid / shared-jar invite /
    /// trusted-channel targets). App token; returns channels that have streamed within the past 6 months.</summary>
    [RequireAction("stream:read")]
    [HttpGet("channels")]
    [ProducesResponseType<StatusResponseDto<List<ChannelSearchDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchChannels([FromQuery] string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Ok(new StatusResponseDto<List<ChannelSearchDto>> { Data = [] });

        Result<TwitchPage<TwitchSearchChannel>> search = await _search.SearchChannelsAsync(
            query,
            liveOnly: null,
            new TwitchPageRequest(),
            ct
        );
        List<ChannelSearchDto> channels = search.IsSuccess
            ?
            [
                .. search.Value.Items.Select(c => new ChannelSearchDto(
                    c.Id,
                    c.DisplayName,
                    c.BroadcasterLogin,
                    c.ThumbnailUrl
                )),
            ]
            : [];

        return Ok(new StatusResponseDto<List<ChannelSearchDto>> { Data = channels });
    }
}
