// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Extensions;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

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
    private readonly IBlockedTrackService _blockedTracks;
    private readonly IMusicHistoryReader _history;
    private readonly ISongRequestIntakeService _intake;
    private readonly IPlaybackStateReader _playbackState;

    public MusicController(
        IMusicService musicService,
        IMusicConfigService configService,
        ISongRequestPageTokenService srPageTokens,
        IBlockedTrackService blockedTracks,
        IMusicHistoryReader history,
        ISongRequestIntakeService intake,
        IPlaybackStateReader playbackState
    )
    {
        _playbackState = playbackState;
        _history = history;
        _intake = intake;
        _musicService = musicService;
        _configService = configService;
        _srPageTokens = srPageTokens;
        _blockedTracks = blockedTracks;
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

    /// <summary>The config a channel with nothing saved resolves to, so the dashboard shows the real defaults.</summary>
    [RequireAction("music:config:read")]
    [HttpGet("config/defaults")]
    [ProducesResponseType<StatusResponseDto<MusicConfigDto>>(StatusCodes.Status200OK)]
    public IActionResult GetConfigDefaults() =>
        Ok(new StatusResponseDto<MusicConfigDto> { Data = _configService.GetDefaults() });

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
    [RequireAction("music:queue:read")]
    [HttpGet("queue")]
    [ProducesResponseType<StatusResponseDto<MusicQueueDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueue(string channelId, CancellationToken ct)
    {
        MusicQueue queue = await _musicService.GetQueueAsync(channelId, ct);

        List<string> requesterNames =
        [
            .. queue
                .Queue.Select(i => i.RequestedBy)
                .Append(queue.CurrentTrack?.RequestedBy)
                .OfType<string>()
                .Distinct(),
        ];
        Dictionary<string, RequesterIdentityDto> requesters = await _history.ResolveRequestersAsync(
            requesterNames,
            ct
        );

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
                queue.CurrentTrack.Provider,
                queue.CurrentTrack.ShuffleEnabled,
                // MusicRepeatMode names (Off/Track/Context) are the wire values ("off"/"track"/"context").
                queue.CurrentTrack.RepeatMode.ToString().ToLowerInvariant(),
                queue.CurrentTrack.CanSetShuffle,
                queue.CurrentTrack.CanSetRepeat,
                queue.CurrentTrack.CanSkipNext,
                queue.CurrentTrack.CanSkipPrevious,
                queue.CurrentTrack.CanSeek,
                queue.CurrentTrack.CanPause,
                queue.CurrentTrack.CanResume,
                RequesterOf(requesters, queue.CurrentTrack.RequestedBy)?.UserId,
                RequesterOf(requesters, queue.CurrentTrack.RequestedBy)?.Color,
                queue.CurrentTrack.TrackUri
            );

        List<QueueItemDto> items =
        [
            .. queue.Queue.Select(
                (item, index) =>
                    new QueueItemDto(
                        index,
                        QueueNumbering.NumberOf(index),
                        item.TrackName,
                        item.Artist,
                        item.ImageUrl,
                        item.DurationMs,
                        item.RequestedBy,
                        item.Cost,
                        RequesterOf(requesters, item.RequestedBy)?.UserId,
                        RequesterOf(requesters, item.RequestedBy)?.Color,
                        item.WaitsForMusicPlayer
                    )
            ),
        ];

        Result<MusicConfigDto> config = await _configService.GetConfigAsync(channelId, ct);
        bool requestsOpen = config.IsFailure
            ? MusicConfigDefaults.RequestsOpen
            : config.Value.RequestsOpen;
        string? blockedReason = Guid.TryParse(channelId, out Guid broadcasterId)
            ? _playbackState.BlockedReasonOf(broadcasterId)
            : null;
        MusicQueueDto dto = new(nowPlaying, items, requestsOpen, blockedReason);
        return Ok(new StatusResponseDto<MusicQueueDto> { Data = dto });
    }

    private static RequesterIdentityDto? RequesterOf(
        Dictionary<string, RequesterIdentityDto> requesters,
        string? name
    ) =>
        name is not null && requesters.TryGetValue(name, out RequesterIdentityDto? who)
            ? who
            : null;

    /// <summary>
    /// Queue a song request by search query, submitted by a viewer or the operator. When the caller
    /// (a viewer using the authenticated dashboard, or a moderator/operator genuinely requesting for
    /// themselves) does not name a target requester, the request is attributed to the authenticated
    /// caller's own display name — never left to fall through to an anonymous/unattributed default,
    /// which would misattribute a real, identifiable requester. An operator queuing on behalf of a
    /// specific viewer still names that viewer explicitly via <see cref="SongRequestDto.RequestedBy"/>.
    /// </summary>
    [RequireAction("music:request:submit")]
    [HttpPost("queue")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddToQueue(
        string channelId,
        [FromBody] SongRequestDto request,
        [FromServices] IRoleResolver roleResolver,
        CancellationToken ct
    )
    {
        string? requestedBy = string.IsNullOrWhiteSpace(request.RequestedBy)
            ? User.GetDisplayName()
            : request.RequestedBy;

        // The caller's own role decides which music services they may request from (one key per service).
        int callerLevel = 0;
        if (
            Guid.TryParse(User.GetUserId(), out Guid userId)
            && Guid.TryParse(channelId, out Guid broadcasterId)
        )
        {
            Result<int> resolved = await roleResolver.ResolveEffectiveLevelAsync(
                userId,
                broadcasterId,
                ct
            );
            callerLevel = resolved.IsSuccess ? resolved.Value : 0;
        }

        Result<MusicTrack> requested = await _musicService.RequestTrackAsync(
            channelId,
            request.Query,
            requestedBy,
            callerLevel,
            ct
        );
        if (requested.IsFailure)
            return ResultResponse(requested);

        return Ok(new StatusResponseDto<object> { Message = "Song added to queue." });
    }

    /// <summary>
    /// Close or open the song queue to new requests. The music keeps playing and the queued songs stay.
    /// </summary>
    [RequireAction("music:queue:moderate")]
    [HttpPut("queue/requests-open")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetRequestsOpen(
        string channelId,
        [FromBody] SetRequestsOpenDto request,
        CancellationToken ct
    )
    {
        Result<bool> changed = await _intake.SetRequestsOpenAsync(channelId, request.Open, ct);
        if (changed.IsFailure)
            return ResultResponse(changed);

        string message = request.Open ? "The song queue is open." : "The song queue is closed.";
        return Ok(new StatusResponseDto<object> { Message = message });
    }

    /// <summary>Remove a queued song at the given position, for moderators clearing bad requests.</summary>
    [RequireAction("music:queue:moderate")]
    [NotDestructive("Removes one queued track; no entity carries a queue-item FK.")]
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

    /// <summary>
    /// Remove every queued song of one requester in a single action (the playing track is untouched).
    /// Returns how many were removed; the queue-changed push reaches every view.
    /// </summary>
    [RequireAction("music:queue:moderate")]
    [NotDestructive("Removes queued tracks; no entity carries a queue-item FK.")]
    [HttpPost("queue/remove-by-requester")]
    [ProducesResponseType<StatusResponseDto<RemovedSongsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveRequesterSongs(
        string channelId,
        [FromBody] RemoveRequesterSongsDto request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            return BadRequestResponse("A requester is required.");

        int removed = await _musicService.RemoveAllFromRequesterAsync(
            channelId,
            request.RequestedBy,
            ct
        );
        return Ok(new StatusResponseDto<RemovedSongsDto> { Data = new RemovedSongsDto(removed) });
    }

    /// <summary>Move a queued song to the front of the queue — play this one next.</summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("queue/{position:int}/promote")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> PromoteToTop(
        string channelId,
        int position,
        CancellationToken ct
    )
    {
        bool moved = await _musicService.PromoteToTopAsync(channelId, position, ct);
        if (!moved)
            return NotFoundResponse($"No queue item at position {position}.");

        return NoContent();
    }

    /// <summary>
    /// Drop a queued song at a chosen position — the drag-and-drop reorder. The item lands exactly
    /// where it was dropped; fair-queue rules for later requests are unchanged.
    /// </summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("queue/{position:int}/move")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MoveInQueue(
        string channelId,
        int position,
        [FromBody] MoveQueueItemDto request,
        CancellationToken ct
    )
    {
        Result result = await _musicService.MoveAsync(channelId, position, request.ToPosition, ct);
        return result.IsSuccess ? NoContent() : ResultResponse(result);
    }

    /// <summary>
    /// Ban the queued song at the given position from future song requests — the dashboard
    /// counterpart to <c>!bansong</c> (which only bans whatever is currently playing). Removes it
    /// from the live queue too.
    /// </summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("queue/{position:int}/ban")]
    [ProducesResponseType<StatusResponseDto<BlockedTrackDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BanQueuedTrack(
        string channelId,
        int position,
        CancellationToken ct
    )
    {
        string? moderatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Result<BlockedTrackDto> result = await _musicService.BanQueuedTrackAsync(
            channelId,
            position,
            moderatorId,
            ct
        );
        return ResultResponse(result);
    }

    // ─── History ─────────────────────────────────────────────────────────────

    /// <summary>The channel's most recent accepted song requests, newest first, with the requester.</summary>
    [RequireAction("music:config:read")]
    [HttpGet("history")]
    [ProducesResponseType<StatusResponseDto<List<SongHistoryItemDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        string channelId,
        [FromQuery] int limit = 12,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        return ResultResponse(
            await _history.GetRecentAsync(broadcasterId, Math.Clamp(limit, 1, 100), ct)
        );
    }

    /// <summary>The viewers who requested the most songs in this channel, all time.</summary>
    [RequireAction("music:config:read")]
    [HttpGet("top-requesters")]
    [ProducesResponseType<StatusResponseDto<List<TopRequesterDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopRequesters(
        string channelId,
        [FromQuery] int limit = 8,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        return ResultResponse(
            await _history.GetTopRequestersAsync(broadcasterId, Math.Clamp(limit, 1, 100), ct)
        );
    }

    // ─── Blocked tracks (legacy !bansong) ─────────────────────────────────────

    /// <summary>List the channel's blocked song-request tracks, newest first, paginated.</summary>
    [RequireAction("music:config:read")]
    [HttpGet("blocked-tracks")]
    [ProducesResponseType<PaginatedResponse<BlockedTrackDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListBlockedTracks(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<BlockedTrackDto>> result = await _blockedTracks.ListAsync(
            broadcasterId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Block a track from song requests — the admission path refuses it from then on.</summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("blocked-tracks")]
    [ProducesResponseType<StatusResponseDto<BlockedTrackDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BlockTrack(
        string channelId,
        [FromBody] BlockTrackRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        return ResultResponse(await _blockedTracks.BlockAsync(broadcasterId, request, ct));
    }

    /// <summary>Unblock a previously blocked track (soft delete), letting it be requested again.</summary>
    [RequireAction("music:queue:moderate")]
    [NotDestructive(
        "Deletes one BlockedTrack row; no entity carries a BlockedTrackId FK and the track can be re-blocked."
    )]
    [HttpDelete("blocked-tracks/{blockedTrackId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnblockTrack(
        string channelId,
        Guid blockedTrackId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _blockedTracks.UnblockAsync(broadcasterId, blockedTrackId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    // ─── Playback controls ────────────────────────────────────────────────────

    /// <summary>Skip the currently playing track to the next one in the queue.</summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("skip")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Skip(string channelId, CancellationToken ct)
    {
        string skippedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        Result result = await _musicService.SkipAsync(channelId, skippedBy, null, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<object> { Message = "Skipped to next track." });
    }

    /// <summary>Pause playback on the channel's active music provider.</summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("pause")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pause(string channelId, CancellationToken ct)
    {
        Result result = await _musicService.PauseAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<object> { Message = "Playback paused." });
    }

    /// <summary>Resume playback on the channel's active music provider.</summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("resume")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resume(string channelId, CancellationToken ct)
    {
        Result result = await _musicService.PlayAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<object> { Message = "Playback resumed." });
    }

    /// <summary>Go back to the previous track; the provider restarts the current one when it is a few seconds in.</summary>
    [RequireAction("music:queue:moderate")]
    [HttpPost("previous")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Previous(string channelId, CancellationToken ct)
    {
        Result result = await _musicService.PreviousAsync(channelId, ct);
        return result.IsSuccess ? NoContent() : ResultResponse(result);
    }

    /// <summary>Set the playback volume on the active music provider (0-100).</summary>
    [RequireAction("music:remote:control")]
    [HttpPut("volume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SetVolume(
        string channelId,
        [FromBody] VolumeDto dto,
        CancellationToken ct
    )
    {
        Result result = await _musicService.SetVolumeAsync(channelId, dto.Percent, ct);
        return result.IsSuccess ? NoContent() : ResultResponse(result);
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
        Result result = await _musicService.SeekAsync(channelId, dto.PositionMs, ct);
        return result.IsSuccess ? NoContent() : ResultResponse(result);
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
        Result result = await _musicService.SetShuffleAsync(channelId, dto.Enabled, ct);
        return result.IsSuccess ? NoContent() : ResultResponse(result);
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
        Result result = await _musicService.SetRepeatAsync(channelId, dto.Mode, ct);
        return result.IsSuccess ? NoContent() : ResultResponse(result);
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
        Result result = await _musicService.TransferPlaybackAsync(
            channelId,
            dto.DeviceId,
            dto.Play,
            ct
        );
        return result.IsSuccess ? NoContent() : ResultResponse(result);
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
    [RequireAction("music:queue:read")]
    [HttpGet("now-playing")]
    [ProducesResponseType<StatusResponseDto<NowPlayingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNowPlaying(string channelId, CancellationToken ct)
    {
        NowPlaying? track = await _musicService.GetNowPlayingAsync(channelId, ct);

        if (track is null)
        {
            // S003 — "nothing is playing" and "the connection is broken" used to look identical here;
            // a broken connection now says so instead of leaving the streamer guessing why playback
            // never shows up.
            string? authStatus = await _musicService.GetActiveProviderAuthStatusAsync(
                channelId,
                ct
            );
            string message = authStatus switch
            {
                "needs_reauth" => "The music connection needs to be reconnected.",
                "forbidden" =>
                    "The music connection doesn't have permission for that — check its access.",
                _ => "Nothing is currently playing.",
            };
            return Ok(new StatusResponseDto<NowPlayingDto> { Data = null, Message = message });
        }

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
            track.Provider,
            track.ShuffleEnabled,
            // MusicRepeatMode names (Off/Track/Context) are the wire values ("off"/"track"/"context").
            track.RepeatMode.ToString().ToLowerInvariant(),
            track.CanSetShuffle,
            track.CanSetRepeat,
            track.CanSkipNext,
            track.CanSkipPrevious,
            track.CanSeek,
            track.CanPause,
            track.CanResume
        );

        if (Guid.TryParse(channelId, out Guid broadcasterId))
        {
            DirectorNowPlaying? shape = DirectorNowPlaying.Build(
                _playbackState.StateOf(broadcasterId),
                _playbackState.LevelOf(broadcasterId),
                track.Provider,
                track.TrackUri,
                track.TrackName,
                track.Artist,
                track.ImageUrl,
                track.DurationMs,
                track.ProgressMs,
                track.IsPlaying,
                track.RequestedBy
            );
            if (shape is not null)
                dto = dto.WithShape(NowPlayingShapeDto.From(shape));
        }

        return Ok(new StatusResponseDto<NowPlayingDto> { Data = dto });
    }
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public sealed record SeekDto(int PositionMs);

public sealed record SetRequestsOpenDto(bool Open);

public sealed record ShuffleDto(bool Enabled);

public sealed record VolumeDto(int Percent);

public sealed record RepeatDto(string Mode);

public sealed record TransferDto(string DeviceId, bool Play = false);

public sealed record PlayContextDto(string ContextUri);
