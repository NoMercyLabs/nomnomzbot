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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The channel's spam defence (spam-defense.md §6) — the weights, what the bot may act on, and the log
/// of everything it has decided.
///
/// <para>Values, bounds and resource keys only: the plain-language explanation of what each knob costs
/// lives in the dashboard's i18n, never in the API, because the product ships in English and Dutch.</para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels")]
[Tags("Moderation")]
public class SpamDefenseController : BaseController
{
    private readonly ISpamDefenseService _spamDefense;

    public SpamDefenseController(ISpamDefenseService spamDefense)
    {
        _spamDefense = spamDefense;
    }

    /// <summary>
    /// The channel's spam-defence configuration: current values, the metadata to render an editor for
    /// them, and the five guarantees that have no switch. A channel that has never edited anything gets
    /// the shipped defaults with <c>isPinned: false</c>, so the dashboard can show what is a default and
    /// what the operator chose.
    /// </summary>
    [HttpGet("{channelId}/spam-defense/policy")]
    [Authorize]
    [RequireAction("spam:policy:read")]
    [ProducesResponseType<StatusResponseDto<SpamDefensePolicyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPolicy(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        return Ok(
            new StatusResponseDto<SpamDefensePolicyDto>
            {
                Data = await _spamDefense.GetPolicyAsync(tenantId, ct),
            }
        );
    }

    /// <summary>
    /// Saves the channel's spam-defence settings, creating the row on first edit.
    ///
    /// <para>Ranges are enforced server-side against the same catalogue the editor renders from, and the
    /// exoneration share must stay below the campaign share — otherwise a group on the borderline would
    /// flip between actioning people and reversing it. Failures name the control by resource key so the
    /// dashboard reports them in the operator's own language.</para>
    /// </summary>
    [HttpPut("{channelId}/spam-defense/policy")]
    [Authorize]
    [RequireAction("spam:policy:manage")]
    [ProducesResponseType<StatusResponseDto<SpamDefenseSettings>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePolicy(
        string channelId,
        [FromBody] SpamDefenseSettings settings,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        Result<SpamDefenseSettings> result = await _spamDefense.UpdateSettingsAsync(
            tenantId,
            settings,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Recorded verdicts, newest first — the review queue, and during dry run the report of what the
    /// system WOULD have done. Reading a week of these is how an operator decides whether to switch
    /// enforcement on at all.
    /// </summary>
    [HttpGet("{channelId}/spam-defense/detections")]
    [Authorize]
    [RequireAction("spam:detections:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<SpamDetectionDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetDetections(
        string channelId,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        return Ok(
            new StatusResponseDto<IReadOnlyList<SpamDetectionDto>>
            {
                Data = await _spamDefense.GetDetectionsAsync(tenantId, page, pageSize, ct),
            }
        );
    }

    /// <summary>
    /// Correlated cohorts — which groups the bot judged coordinated, which it exonerated, and how many
    /// accounts each one actually touched.
    /// </summary>
    [HttpGet("{channelId}/spam-defense/campaigns")]
    [Authorize]
    [RequireAction("spam:detections:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<SpamCampaignDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetCampaigns(
        string channelId,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        return Ok(
            new StatusResponseDto<IReadOnlyList<SpamCampaignDto>>
            {
                Data = await _spamDefense.GetCampaignsAsync(tenantId, page, pageSize, ct),
            }
        );
    }

    /// <summary>Follow-bot blocks, each with the per-account evidence that justified it (SD9).</summary>
    [HttpGet("{channelId}/spam-defense/follow-bot-blocks")]
    [Authorize]
    [RequireAction("spam:detections:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<FollowBotBlockDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetFollowBotBlocks(
        string channelId,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        return Ok(
            new StatusResponseDto<IReadOnlyList<FollowBotBlockDto>>
            {
                Data = await _spamDefense.GetFollowBotBlocksAsync(tenantId, page, pageSize, ct),
            }
        );
    }

    /// <summary>
    /// Restores an entire spike batch at once. Bulk by design: a misread viral moment can be hundreds
    /// of accounts, and undoing them one at a time is not a recovery path anybody would use.
    /// </summary>
    [HttpPost("{channelId}/spam-defense/follow-bot-blocks/{batchId}/restore")]
    [Authorize]
    [RequireAction("spam:detections:manage")]
    [ProducesResponseType<StatusResponseDto<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RestoreFollowBotBatch(
        string channelId,
        string batchId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");
        if (!Guid.TryParse(batchId, out Guid batch))
            return BadRequestResponse("Invalid batch id.");

        return ResultResponse(await _spamDefense.RestoreFollowBotBatchAsync(tenantId, batch, ct));
    }

    /// <summary>
    /// Marks a verdict wrong. Moderator-level on purpose: this is the correction path, and making it
    /// owner-only would leave moderators watching false positives they cannot fix.
    /// </summary>
    [HttpPost("{channelId}/spam-defense/detections/{detectionId}/overturn")]
    [Authorize]
    [RequireAction("spam:detections:manage")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OverturnDetection(
        string channelId,
        string detectionId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");
        if (!Guid.TryParse(detectionId, out Guid id))
            return BadRequestResponse("Invalid detection id.");

        Result result = await _spamDefense.OverturnDetectionAsync(tenantId, id, ct);
        return ResultResponse(result);
    }
}
