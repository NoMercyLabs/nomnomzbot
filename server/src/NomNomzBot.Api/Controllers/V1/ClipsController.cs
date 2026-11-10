// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.YouTube;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The channel's Twitch clips and their way to the streamer's YouTube channel. Both actions sit behind the
/// clip write key: the list exists to choose a clip to upload.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/clips")]
[Authorize]
[Tags("Clips")]
public class ClipsController(ITwitchClipsApi clips, IYouTubeClipUploadService uploads)
    : BaseController
{
    private const int PageSize = 20;

    /// <summary>One page of the channel's clips, newest first, and the YouTube uploads left today.</summary>
    [HttpGet]
    [RequireAction("live-ops:clips:write")]
    [ProducesResponseType<StatusResponseDto<ChannelClipsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClips(
        Guid channelId,
        [FromQuery] string? cursor,
        CancellationToken ct
    )
    {
        Result<TwitchPage<TwitchClip>> page = await clips.GetClipsByBroadcasterAsync(
            channelId,
            new(cursor, PageSize),
            ct
        );
        if (page.IsFailure)
            return TwitchResultResponse(page);

        return ResultResponse(
            Result.Success(
                new ChannelClipsDto(
                    [
                        .. page.Value.Items.Select(clip => new ChannelClipDto(
                            clip.Id,
                            clip.Title,
                            clip.Url,
                            clip.ThumbnailUrl,
                            clip.CreatorName,
                            clip.CreatedAt,
                            clip.ViewCount,
                            clip.Duration
                        )),
                    ],
                    page.Value.NextCursor,
                    await uploads.UploadsLeftTodayAsync(ct)
                )
            )
        );
    }

    /// <summary>
    /// Put one clip on the streamer's YouTube channel. <c>outcome</c> says what happened; a video is private
    /// unless the request chooses another privacy.
    /// </summary>
    [HttpPost("{clipId}/youtube")]
    [RequireAction("live-ops:clips:write")]
    [ProducesResponseType<StatusResponseDto<YouTubeClipUploadDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadToYouTube(
        Guid channelId,
        string clipId,
        [FromBody] YouTubeClipUploadRequest request,
        CancellationToken ct
    ) => ResultResponse(await uploads.UploadAsync(channelId, clipId, request, ct));
}

/// <summary>One clip in the list a streamer chooses from.</summary>
public sealed record ChannelClipDto(
    string Id,
    string Title,
    string Url,
    string ThumbnailUrl,
    string CreatorName,
    DateTimeOffset CreatedAt,
    int ViewCount,
    double DurationSeconds
);

/// <summary>A page of clips, the cursor of the next page, and the YouTube uploads the app has left today.</summary>
public sealed record ChannelClipsDto(
    IReadOnlyList<ChannelClipDto> Clips,
    string? NextCursor,
    int UploadsLeftToday
);
