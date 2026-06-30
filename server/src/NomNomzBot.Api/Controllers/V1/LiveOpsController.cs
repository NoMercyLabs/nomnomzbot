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

    public LiveOpsController(
        ITwitchPollsApi polls,
        ITwitchPredictionsApi predictions,
        ITwitchRaidsApi raids,
        ITwitchAdsApi ads,
        ITwitchClipsApi clips
    )
    {
        _polls = polls;
        _predictions = predictions;
        _raids = raids;
        _ads = ads;
        _clips = clips;
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
}
