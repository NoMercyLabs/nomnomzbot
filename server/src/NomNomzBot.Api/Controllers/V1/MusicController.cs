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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Manages a channel's connected music integration — configuration, queue, playback controls,
/// and song requests — for the dashboard operator and viewers using the public song-request page.
/// </summary>
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

    /// <summary>Get the channel's music integration configuration for the dashboard operator.</summary>
    [RequireAction("music:config:read")]
    [HttpGet("config")]
    [ProducesResponseType<StatusResponseDto<MusicConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfig(string channelId, CancellationToken ct)
    {
        Result<MusicConfigDto> result = await _configService.GetConfigAsync(channelId, ct);
        return ResultResponse(result);
    }

    /// <summary>Update the channel's music integration configuration.</summary>
    [RequireAction("music:config:write")]
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
    [RequireAction("music:token:read")]
    [HttpGet("sr-page-token")]
    [ProducesResponseType<StatusResponseDto<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSrPageToken(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await _srPageTokens.GetOrCreateAsync(broadcasterId, ct));
    }

    /// <summary>Rotates the SR-page token, revoking public access via the old /sr link.</summary>
    [RequireAction("music:token:rotate")]
    [HttpPost("sr-page-token/rotate")]
    [ProducesResponseType<StatusResponseDto<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RotateSrPageToken(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await _srPageTokens.RotateAsync(broadcasterId, ct));
    }

    // ─── Queue ───────────────────────────────────────────────────────────────

    /// <summary>Get the currently playing track and the upcoming song queue for the channel.</summary>
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

    /// <summary>Queue a song request by search query, submitted by a viewer or the operator.</summary>
    [RequireAction("music:request:submit")]
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

    /// <summary>Remove a queued song at the given position, for moderators clearing bad requests.</summary>
    [RequireAction("music:queue:moderate")]
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

    /// <summary>Skip the currently playing track to the next one in the queue.</summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("skip")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Skip(string channelId, CancellationToken ct)
    {
        bool ok = await _musicService.SkipAsync(channelId, ct);
        if (!ok)
            return ServiceUnavailableResponse("No active music provider.");
        return Ok(new StatusResponseDto<object> { Message = "Skipped to next track." });
    }

    /// <summary>Pause playback on the channel's active music provider.</summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("pause")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pause(string channelId, CancellationToken ct)
    {
        bool ok = await _musicService.PauseAsync(channelId, ct);
        if (!ok)
            return ServiceUnavailableResponse("No active music provider.");
        return Ok(new StatusResponseDto<object> { Message = "Playback paused." });
    }

    /// <summary>Resume playback on the channel's active music provider.</summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("resume")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resume(string channelId, CancellationToken ct)
    {
        bool ok = await _musicService.PlayAsync(channelId, ct);
        if (!ok)
            return ServiceUnavailableResponse("No active music provider.");
        return Ok(new StatusResponseDto<object> { Message = "Playback resumed." });
    }

    // ─── Remote controls (seek / shuffle / repeat / transfer / playlists) ───────

    /// <summary>Seek the currently playing track to a specific position in milliseconds.</summary>
    [RequireAction("music:remote:control")]
    [HttpPost("seek")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Seek(
        string channelId,
        [FromBody] SeekDto dto,
        CancellationToken ct
    )
    {
        bool ok = await _musicService.SeekAsync(channelId, dto.PositionMs, ct);
        return ok
            ? NoContent()
            : ServiceUnavailableResponse("Seek not supported by active provider.");
    }

    /// <summary>Turn shuffle mode on or off on the active music provider.</summary>
    [RequireAction("music:remote:control")]
    [HttpPatch("shuffle")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SetShuffle(
        string channelId,
        [FromBody] ShuffleDto dto,
        CancellationToken ct
    )
    {
        bool ok = await _musicService.SetShuffleAsync(channelId, dto.Enabled, ct);
        return ok
            ? NoContent()
            : ServiceUnavailableResponse("Shuffle not supported by active provider.");
    }

    /// <summary>Set the repeat mode on the active music provider.</summary>
    [RequireAction("music:remote:control")]
    [HttpPatch("repeat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SetRepeat(
        string channelId,
        [FromBody] RepeatDto dto,
        CancellationToken ct
    )
    {
        bool ok = await _musicService.SetRepeatAsync(channelId, dto.Mode, ct);
        return ok
            ? NoContent()
            : ServiceUnavailableResponse("Repeat not supported by active provider.");
    }

    /// <summary>List the playback devices available on the channel's active music provider.</summary>
    [RequireAction("music:remote:control")]
    [HttpGet("devices")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<MusicDeviceDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetDevices(string channelId, CancellationToken ct)
    {
        IReadOnlyList<MusicDeviceDto> devices = await _musicService.GetDevicesAsync(channelId, ct);
        return Ok(new StatusResponseDto<IReadOnlyList<MusicDeviceDto>> { Data = devices });
    }

    /// <summary>Transfer playback to a different device, optionally resuming playback immediately.</summary>
    [RequireAction("music:remote:control")]
    [HttpPost("transfer")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Transfer(
        string channelId,
        [FromBody] TransferDto dto,
        CancellationToken ct
    )
    {
        bool ok = await _musicService.TransferPlaybackAsync(channelId, dto.DeviceId, dto.Play, ct);
        return ok
            ? NoContent()
            : ServiceUnavailableResponse("Transfer not supported by active provider.");
    }

    /// <summary>List the connected music account's playlists, paginated by offset and limit.</summary>
    [RequireAction("music:library:write")]
    [HttpGet("playlists")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<MusicPlaylistDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetPlaylists(
        string channelId,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<MusicPlaylistDto> playlists = await _musicService.GetPlaylistsAsync(
            channelId,
            offset,
            limit,
            ct
        );
        return Ok(new StatusResponseDto<IReadOnlyList<MusicPlaylistDto>> { Data = playlists });
    }

    /// <summary>Start playback of a playlist, album, or other provider context by URI.</summary>
    [RequireAction("music:remote:control")]
    [HttpPost("play-context")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PlayContext(
        string channelId,
        [FromBody] PlayContextDto dto,
        CancellationToken ct
    )
    {
        bool ok = await _musicService.PlayContextAsync(channelId, dto.ContextUri, ct);
        return ok
            ? NoContent()
            : ServiceUnavailableResponse("Play context not supported by active provider.");
    }

    // ─── Now playing ──────────────────────────────────────────────────────────

    /// <summary>Get the track currently playing on the channel's active music provider, if any.</summary>
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

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public sealed record SeekDto(int PositionMs);

public sealed record ShuffleDto(bool Enabled);

public sealed record RepeatDto(string Mode);

public sealed record TransferDto(string DeviceId, bool Play = false);

public sealed record PlayContextDto(string ContextUri);
