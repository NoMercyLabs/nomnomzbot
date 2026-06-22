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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/music")]
[Authorize]
[Tags("Music")]
public class MusicController : BaseController
{
    private readonly IMusicService _musicService;
    private readonly IMusicConfigService _configService;
    private readonly ISongRequestPageTokenService _srPageTokens;

    public MusicController(
        IMusicService musicService,
        IMusicConfigService configService,
        ISongRequestPageTokenService srPageTokens
    )
    {
        _musicService = musicService;
        _configService = configService;
        _srPageTokens = srPageTokens;
    }

    // ─── Configuration ────────────────────────────────────────────────────────

    [HttpGet("config")]
    [ProducesResponseType<StatusResponseDto<MusicConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfig(string channelId, CancellationToken ct)
    {
        Result<MusicConfigDto> result = await _configService.GetConfigAsync(channelId, ct);
        return ResultResponse(result);
    }

    [HttpPut("config")]
    [ProducesResponseType<StatusResponseDto<MusicConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfig(
        string channelId,
        [FromBody] UpdateMusicConfigDto request,
        CancellationToken ct
    )
    {
        Result<MusicConfigDto> result = await _configService.UpdateConfigAsync(
            channelId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<MusicConfigDto> { Data = result.Value });
    }

    // ─── Public SR-page token (music-sr.md §3.7) ───────────────────────────────

    /// <summary>Returns this channel's public SR-page token, minting one on first call (the shareable /sr link).</summary>
    [HttpGet("sr-page-token")]
    [ProducesResponseType<StatusResponseDto<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSrPageToken(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await _srPageTokens.GetOrCreateAsync(broadcasterId, ct));
    }

    /// <summary>Rotates the SR-page token, revoking public access via the old /sr link.</summary>
    [HttpPost("sr-page-token/rotate")]
    [ProducesResponseType<StatusResponseDto<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RotateSrPageToken(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await _srPageTokens.RotateAsync(broadcasterId, ct));
    }

    // ─── Queue ───────────────────────────────────────────────────────────────

    [HttpGet("queue")]
    [ProducesResponseType<StatusResponseDto<MusicQueueDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueue(string channelId, CancellationToken ct)
    {
        MusicQueue queue = await _musicService.GetQueueAsync(channelId, ct);

        NowPlayingDto? nowPlaying = queue.CurrentTrack is null
            ? null
            : new NowPlayingDto(
                queue.CurrentTrack.TrackName,
                queue.CurrentTrack.Artist,
                queue.CurrentTrack.Album,
                queue.CurrentTrack.ImageUrl,
                queue.CurrentTrack.DurationMs,
                queue.CurrentTrack.ProgressMs,
                queue.CurrentTrack.IsPlaying,
                queue.CurrentTrack.Volume,
                queue.CurrentTrack.RequestedBy,
                queue.CurrentTrack.Provider
            );

        List<QueueItemDto> items = queue
            .Queue.Select(
                (item, index) =>
                    new QueueItemDto(
                        index,
                        item.TrackName,
                        item.Artist,
                        item.ImageUrl,
                        item.DurationMs,
                        item.RequestedBy
                    )
            )
            .ToList();

        MusicQueueDto dto = new(nowPlaying, items);
        return Ok(new StatusResponseDto<MusicQueueDto> { Data = dto });
    }

    [HttpPost("queue")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddToQueue(
        string channelId,
        [FromBody] SongRequestDto request,
        CancellationToken ct
    )
    {
        bool added = await _musicService.AddToQueueAsync(
            channelId,
            request.Query,
            request.RequestedBy,
            ct
        );
        if (!added)
            return ServiceUnavailableResponse(
                "Music service is unavailable or no provider is connected."
            );

        return Ok(new StatusResponseDto<object> { Message = "Song added to queue." });
    }

    [HttpDelete("queue/{position:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveFromQueue(
        string channelId,
        int position,
        CancellationToken ct
    )
    {
        bool removed = await _musicService.RemoveFromQueueAsync(channelId, position, ct);
        if (!removed)
            return NotFoundResponse($"No queue item at position {position}.");

        return NoContent();
    }

    // ─── Playback controls ────────────────────────────────────────────────────

    [HttpPost("skip")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Skip(string channelId, CancellationToken ct)
    {
        bool ok = await _musicService.SkipAsync(channelId, ct);
        if (!ok)
            return ServiceUnavailableResponse("No active music provider.");
        return Ok(new StatusResponseDto<object> { Message = "Skipped to next track." });
    }

    [HttpPost("pause")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pause(string channelId, CancellationToken ct)
    {
        bool ok = await _musicService.PauseAsync(channelId, ct);
        if (!ok)
            return ServiceUnavailableResponse("No active music provider.");
        return Ok(new StatusResponseDto<object> { Message = "Playback paused." });
    }

    [HttpPost("resume")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resume(string channelId, CancellationToken ct)
    {
        bool ok = await _musicService.PlayAsync(channelId, ct);
        if (!ok)
            return ServiceUnavailableResponse("No active music provider.");
        return Ok(new StatusResponseDto<object> { Message = "Playback resumed." });
    }

    // ─── Now playing ──────────────────────────────────────────────────────────

    [HttpGet("now-playing")]
    [ProducesResponseType<StatusResponseDto<NowPlayingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNowPlaying(string channelId, CancellationToken ct)
    {
        NowPlaying? track = await _musicService.GetNowPlayingAsync(channelId, ct);

        if (track is null)
            return Ok(
                new StatusResponseDto<NowPlayingDto>
                {
                    Data = null,
                    Message = "Nothing is currently playing.",
                }
            );

        NowPlayingDto dto = new(
            track.TrackName,
            track.Artist,
            track.Album,
            track.ImageUrl,
            track.DurationMs,
            track.ProgressMs,
            track.IsPlaying,
            track.Volume,
            track.RequestedBy,
            track.Provider
        );

        return Ok(new StatusResponseDto<NowPlayingDto> { Data = dto });
    }
}
