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
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages live stream operations: polls, predictions, raids, commercials, and clips.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/live-ops")]
[Authorize]
[Tags("LiveOps")]
public class LiveOpsController : BaseController
{
    private readonly ITwitchPollsApi _polls;
    private readonly ITwitchPredictionsApi _predictions;
    private readonly ITwitchRaidsApi _raids;
    private readonly ITwitchAdsApi _ads;
    private readonly ITwitchClipsApi _clips;
    private readonly ITwitchScheduleApi _schedule;
    private readonly ITwitchStreamsApi _streams;

    public LiveOpsController(
        ITwitchPollsApi polls,
        ITwitchPredictionsApi predictions,
        ITwitchRaidsApi raids,
        ITwitchAdsApi ads,
        ITwitchClipsApi clips,
        ITwitchScheduleApi schedule,
        ITwitchStreamsApi streams
    )
    {
        _polls = polls;
        _predictions = predictions;
        _raids = raids;
        _ads = ads;
        _clips = clips;
        _schedule = schedule;
        _streams = streams;
    }

    // ─── Polls ────────────────────────────────────────────────────────────────

    public record CreatePollDto(
        string Title,
        List<string> Choices,
        int DurationSeconds,
        bool ChannelPointsVotingEnabled = false,
        int ChannelPointsPerVote = 0
    );

    public record EndPollDto(string Status);

    /// <summary>List active and ended polls for the channel.</summary>
    [RequireAction("live-ops:polls:read")]
    [HttpGet("polls")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPolls(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchPage<TwitchPoll>> result = await _polls.GetPollsAsync(
            broadcasterId,
            null,
            new TwitchPageRequest(After: null, PageSize: 10),
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(new StatusResponseDto<IReadOnlyList<TwitchPoll>> { Data = result.Value.Items });
    }

    /// <summary>Create a new poll with optional channel point voting.</summary>
    [RequireAction("live-ops:polls:write")]
    [HttpPost("polls")]
    [ProducesResponseType<StatusResponseDto<TwitchPoll>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePoll(
        string channelId,
        [FromBody] CreatePollDto dto,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        List<CreatePollChoiceRequest> choices = dto
            .Choices.Select(c => new CreatePollChoiceRequest(c))
            .ToList();

        CreatePollRequest request = new(
            dto.Title,
            choices,
            dto.DurationSeconds,
            dto.ChannelPointsVotingEnabled ? true : null,
            dto.ChannelPointsVotingEnabled && dto.ChannelPointsPerVote > 0
                ? dto.ChannelPointsPerVote
                : null
        );

        Result<TwitchPoll> result = await _polls.CreatePollAsync(broadcasterId, request, ct);
        return result.IsFailure
            ? TwitchResultResponse(result)
            : StatusCode(
                StatusCodes.Status201Created,
                new StatusResponseDto<TwitchPoll> { Data = result.Value, Message = "Poll created." }
            );
    }

    /// <summary>End a poll with a specified outcome (ARCHIVED or TERMINATED).</summary>
    [RequireAction("live-ops:polls:write")]
    [HttpPatch("polls/{pollId}/end")]
    [ProducesResponseType<StatusResponseDto<TwitchPoll>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EndPoll(
        string channelId,
        string pollId,
        [FromBody] EndPollDto dto,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchPoll> result = await _polls.EndPollAsync(
            broadcasterId,
            pollId,
            dto.Status,
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(new StatusResponseDto<TwitchPoll> { Data = result.Value });
    }

    // ─── Predictions ──────────────────────────────────────────────────────────

    public record CreatePredictionDto(
        string Title,
        List<string> Outcomes,
        int PredictionWindowSeconds
    );

    public record EndPredictionDto(string Status, string? WinningOutcomeId = null);

    /// <summary>List active and ended predictions for the channel.</summary>
    [RequireAction("live-ops:predictions:read")]
    [HttpGet("predictions")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPredictions(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchPage<TwitchPrediction>> result = await _predictions.GetPredictionsAsync(
            broadcasterId,
            null,
            new TwitchPageRequest(After: null, PageSize: 10),
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(
                new StatusResponseDto<IReadOnlyList<TwitchPrediction>> { Data = result.Value.Items }
            );
    }

    /// <summary>Create a new channel prediction with multiple outcomes.</summary>
    [RequireAction("live-ops:predictions:write")]
    [HttpPost("predictions")]
    [ProducesResponseType<StatusResponseDto<TwitchPrediction>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePrediction(
        string channelId,
        [FromBody] CreatePredictionDto dto,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        List<CreatePredictionOutcome> outcomes = dto
            .Outcomes.Select(o => new CreatePredictionOutcome(o))
            .ToList();

        CreatePredictionRequest request = new(dto.Title, outcomes, dto.PredictionWindowSeconds);

        Result<TwitchPrediction> result = await _predictions.CreatePredictionAsync(
            broadcasterId,
            request,
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : StatusCode(
                StatusCodes.Status201Created,
                new StatusResponseDto<TwitchPrediction>
                {
                    Data = result.Value,
                    Message = "Prediction created.",
                }
            );
    }

    /// <summary>End a prediction with RESOLVED or CANCELED status and optional winning outcome.</summary>
    [RequireAction("live-ops:predictions:write")]
    [HttpPatch("predictions/{predictionId}/end")]
    [ProducesResponseType<StatusResponseDto<TwitchPrediction>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EndPrediction(
        string channelId,
        string predictionId,
        [FromBody] EndPredictionDto dto,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchPrediction> result = await _predictions.EndPredictionAsync(
            broadcasterId,
            predictionId,
            dto.Status,
            dto.WinningOutcomeId,
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(new StatusResponseDto<TwitchPrediction> { Data = result.Value });
    }

    // ─── Raids ────────────────────────────────────────────────────────────────

    public record StartRaidDto(string TargetTwitchBroadcasterId);

    /// <summary>Start a raid to another channel.</summary>
    [RequireAction("live-ops:raids:write")]
    [HttpPost("raids")]
    [ProducesResponseType<StatusResponseDto<TwitchRaid>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> StartRaid(
        string channelId,
        [FromBody] StartRaidDto dto,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchRaid> result = await _raids.StartRaidAsync(
            broadcasterId,
            dto.TargetTwitchBroadcasterId,
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : StatusCode(
                StatusCodes.Status201Created,
                new StatusResponseDto<TwitchRaid> { Data = result.Value, Message = "Raid started." }
            );
    }

    /// <summary>Cancel a raid that has not yet begun.</summary>
    [RequireAction("live-ops:raids:write")]
    [HttpDelete("raids")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelRaid(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _raids.CancelRaidAsync(broadcasterId, ct);
        return result.IsFailure ? TwitchResultResponse(result) : NoContent();
    }

    // ─── Ads ──────────────────────────────────────────────────────────────────

    public record StartCommercialDto(int LengthSeconds);

    /// <summary>Get the channel's scheduled ad breaks from Twitch.</summary>
    [RequireAction("live-ops:ads:read")]
    [HttpGet("ads/schedule")]
    [ProducesResponseType<StatusResponseDto<TwitchAdSchedule>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAdSchedule(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchAdSchedule> result = await _ads.GetAdScheduleAsync(broadcasterId, ct);
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(new StatusResponseDto<TwitchAdSchedule> { Data = result.Value });
    }

    /// <summary>Start a commercial break of specified length (30, 60, 90, 120, 150, or 180 seconds).</summary>
    [RequireAction("live-ops:ads:write")]
    [HttpPost("ads/commercial")]
    [ProducesResponseType<StatusResponseDto<TwitchCommercial>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StartCommercial(
        string channelId,
        [FromBody] StartCommercialDto dto,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchCommercial> result = await _ads.StartCommercialAsync(
            broadcasterId,
            dto.LengthSeconds,
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(new StatusResponseDto<TwitchCommercial> { Data = result.Value });
    }

    /// <summary>Delay the next scheduled ad break by 8 minutes.</summary>
    [RequireAction("live-ops:ads:write")]
    [HttpPost("ads/snooze")]
    [ProducesResponseType<StatusResponseDto<TwitchAdSnooze>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SnoozeNextAd(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchAdSnooze> result = await _ads.SnoozeNextAdAsync(broadcasterId, ct);
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(new StatusResponseDto<TwitchAdSnooze> { Data = result.Value });
    }

    // ─── Clips ────────────────────────────────────────────────────────────────

    /// <summary>Create a clip of the current broadcast.</summary>
    [RequireAction("live-ops:clips:write")]
    [HttpPost("clips")]
    [ProducesResponseType<StatusResponseDto<TwitchClipStub>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateClip(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchClipStub> result = await _clips.CreateClipAsync(broadcasterId, false, ct);
        return result.IsFailure
            ? TwitchResultResponse(result)
            : StatusCode(
                StatusCodes.Status201Created,
                new StatusResponseDto<TwitchClipStub>
                {
                    Data = result.Value,
                    Message = "Clip created.",
                }
            );
    }

    // ─── Stream schedule ────────────────────────────────────────────────────────
    // Helix "Schedule" category (broadcaster-liveops.md §3.5). Read-through — no local table; each write is
    // a Helix mutation. Segment CRUD + the vacation window; the iCalendar feed is served verbatim.

    /// <summary>Toggle a vacation and/or (re)schedule its window (Helix PATCH /schedule/settings).</summary>
    public record UpdateScheduleSettingsDto(
        bool? IsVacationEnabled,
        DateTimeOffset? VacationStartTime,
        DateTimeOffset? VacationEndTime,
        string? Timezone
    );

    /// <summary>Create a stream marker at the current live position (Helix POST /streams/markers).</summary>
    public record CreateMarkerDto(string? Description = null);

    /// <summary>Get the channel's stream schedule (segments + active vacation window) from Twitch.</summary>
    [RequireAction("live-ops:schedule:read")]
    [HttpGet("schedule")]
    [ProducesResponseType<StatusResponseDto<TwitchSchedule>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSchedule(
        string channelId,
        [FromQuery] string? after,
        [FromQuery] int pageSize,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchSchedule> result = await _schedule.GetScheduleAsync(
            broadcasterId,
            new TwitchPageRequest(after, pageSize <= 0 ? 100 : pageSize),
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(new StatusResponseDto<TwitchSchedule> { Data = result.Value });
    }

    /// <summary>Get the channel's schedule as an iCalendar feed (RFC 5545, text/calendar).</summary>
    [RequireAction("live-ops:schedule:read")]
    [HttpGet("schedule/icalendar")]
    [Produces("text/calendar")]
    public async Task<IActionResult> GetScheduleICalendar(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<string> result = await _schedule.GetICalendarAsync(broadcasterId, ct);
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Content(result.Value, "text/calendar");
    }

    /// <summary>Add a single or recurring broadcast segment to the schedule (Helix POST /schedule/segment).</summary>
    [RequireAction("live-ops:schedule:write")]
    [HttpPost("schedule/segment")]
    [ProducesResponseType<StatusResponseDto<TwitchSchedule>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateScheduleSegment(
        string channelId,
        [FromBody] CreateScheduleSegmentRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchSchedule> result = await _schedule.CreateSegmentAsync(
            broadcasterId,
            request,
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(new StatusResponseDto<TwitchSchedule> { Data = result.Value });
    }

    /// <summary>Edit a scheduled segment (Helix PATCH /schedule/segment).</summary>
    [RequireAction("live-ops:schedule:write")]
    [HttpPatch("schedule/segment/{segmentId}")]
    [ProducesResponseType<StatusResponseDto<TwitchSchedule>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateScheduleSegment(
        string channelId,
        string segmentId,
        [FromBody] UpdateScheduleSegmentRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchSchedule> result = await _schedule.UpdateSegmentAsync(
            broadcasterId,
            segmentId,
            request,
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(new StatusResponseDto<TwitchSchedule> { Data = result.Value });
    }

    /// <summary>Remove a segment from the schedule (Helix DELETE /schedule/segment).</summary>
    [RequireAction("live-ops:schedule:write")]
    [HttpDelete("schedule/segment/{segmentId}")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteScheduleSegment(
        string channelId,
        string segmentId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _schedule.DeleteSegmentAsync(broadcasterId, segmentId, ct);
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(
                new StatusResponseDto<bool> { Data = true, Message = "Schedule segment deleted." }
            );
    }

    /// <summary>Toggle or schedule the broadcaster's vacation window (Helix PATCH /schedule/settings).</summary>
    [RequireAction("live-ops:schedule:write")]
    [HttpPut("schedule/settings")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateScheduleSettings(
        string channelId,
        [FromBody] UpdateScheduleSettingsDto dto,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _schedule.UpdateScheduleSettingsAsync(
            broadcasterId,
            dto.IsVacationEnabled,
            dto.VacationStartTime,
            dto.VacationEndTime,
            dto.Timezone,
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : Ok(
                new StatusResponseDto<bool> { Data = true, Message = "Schedule settings updated." }
            );
    }

    // ─── Stream markers ───────────────────────────────────────────────────────
    // Helix "Streams" marker endpoint (broadcaster-liveops.md §3.6). Stateless — a marker on the current
    // live VOD position. Twitch rejects the call when the channel is offline (surfaced as the Twitch error).

    /// <summary>Mark the current live position for later VOD review (Helix POST /streams/markers).</summary>
    [RequireAction("live-ops:marker:create")]
    [HttpPost("markers")]
    [ProducesResponseType<StatusResponseDto<TwitchStreamMarker>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateStreamMarker(
        string channelId,
        [FromBody] CreateMarkerDto dto,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<TwitchStreamMarker> result = await _streams.CreateStreamMarkerAsync(
            broadcasterId,
            dto.Description,
            ct
        );
        return result.IsFailure
            ? TwitchResultResponse(result)
            : StatusCode(
                StatusCodes.Status201Created,
                new StatusResponseDto<TwitchStreamMarker>
                {
                    Data = result.Value,
                    Message = "Stream marker created.",
                }
            );
    }
}
