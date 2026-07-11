// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Application service for moderation actions and auto-moderation rule management.
/// </summary>
public interface IModerationService
{
    /// <summary>Timeout a user in a channel.</summary>
    Task<Result<ModerationActionResult>> TimeoutAsync(
        string broadcasterId,
        string targetUserId,
        int durationSeconds,
        string? reason = null,
        string? moderatorId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Ban a user from a channel.</summary>
    Task<Result<ModerationActionResult>> BanAsync(
        string broadcasterId,
        string targetUserId,
        string? reason = null,
        string? moderatorId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Unban a user in a channel.</summary>
    Task<Result<ModerationActionResult>> UnbanAsync(
        string broadcasterId,
        string targetUserId,
        string? moderatorId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Create an auto-moderation rule.</summary>
    Task<Result<ModerationRuleDetail>> CreateRuleAsync(
        string broadcasterId,
        CreateModerationRuleRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Delete an auto-moderation rule.</summary>
    Task<Result> DeleteRuleAsync(
        string broadcasterId,
        int ruleId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Update an existing moderation rule.</summary>
    Task<Result<ModerationRuleDetail>> UpdateRuleAsync(
        string broadcasterId,
        int ruleId,
        UpdateModerationRuleRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>List all moderation rules in a channel.</summary>
    Task<Result<PagedList<ModerationRuleListItem>>> ListRulesAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get moderation action history for a channel.</summary>
    Task<Result<PagedList<ModerationActionLog>>> GetActionsAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get the auto-moderation config (link filter, caps filter, banned phrases, emote spam).</summary>
    Task<Result<AutomodConfigDto>> GetAutomodConfigAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Save the auto-moderation config, upserting the four built-in rule types.</summary>
    Task<Result<AutomodConfigDto>> SaveAutomodConfigAsync(
        string broadcasterId,
        AutomodConfigDto config,
        CancellationToken cancellationToken = default
    );

    /// <summary>List users permanently banned in a channel, read live from the Twitch moderation API.</summary>
    Task<Result<List<BannedUserDto>>> GetBannedUsersAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>List the channel's blocked terms (text only), read live from the Twitch moderation API.</summary>
    Task<Result<List<string>>> GetBlockedTermsAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Add a blocked term on Twitch and return the refreshed list.</summary>
    Task<Result<List<string>>> AddBlockedTermAsync(
        string broadcasterId,
        string text,
        CancellationToken cancellationToken = default
    );

    /// <summary>Remove a blocked term (matched by its text) on Twitch and return the refreshed list.</summary>
    Task<Result<List<string>>> RemoveBlockedTermAsync(
        string broadcasterId,
        string text,
        CancellationToken cancellationToken = default
    );

    /// <summary>The channel's current Shield Mode activation, read live from the Twitch moderation API.</summary>
    Task<Result<bool>> GetShieldModeAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Activate or deactivate Shield Mode on Twitch and return the applied state.</summary>
    Task<Result<bool>> SetShieldModeAsync(
        string broadcasterId,
        bool isActive,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// List the channel's unban requests filtered by <paramref name="status"/> (pending / approved / denied /
    /// acknowledged / canceled), read live from the Twitch moderation API.
    /// </summary>
    Task<Result<List<UnbanRequestDto>>> GetUnbanRequestsAsync(
        string broadcasterId,
        string status,
        CancellationToken cancellationToken = default
    );

    /// <summary>Approve or deny an unban request on Twitch, returning the resolved request.</summary>
    Task<Result<UnbanRequestDto>> ResolveUnbanRequestAsync(
        string broadcasterId,
        string unbanRequestId,
        bool approve,
        string? note,
        CancellationToken cancellationToken = default
    );
}
