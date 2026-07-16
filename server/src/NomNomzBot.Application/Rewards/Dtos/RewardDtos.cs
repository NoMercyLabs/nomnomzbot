// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Rewards.Dtos;

/// <summary>One entry in the channel-points redemption queue (rewards.md) — a redemption with its current
/// status (<c>unfulfilled</c> / <c>fulfilled</c> / <c>canceled</c>), folded from the journal.</summary>
public sealed record RedemptionListItem(
    string RedemptionId,
    string RewardId,
    string RewardTitle,
    string UserId,
    string UserDisplayName,
    int Cost,
    string? UserInput,
    string Status,
    DateTime RedeemedAt
);

public sealed record RewardDetail(
    string Id,
    string Title,
    string? Prompt,
    int Cost,
    bool IsEnabled,
    // True when the bot created this reward under its own Twitch client and may edit/delete it; false for
    // externally-created rewards (Twitch UI / other apps) the dashboard must show as read-only.
    bool IsManageable,
    bool IsUserInputRequired,
    bool IsPaused,
    string? BackgroundColor,
    string? ImageUrl,
    int? MaxPerStream,
    int? MaxPerUserPerStream,
    int? GlobalCooldownSeconds,
    string? ActionType,
    Dictionary<string, object?>? ActionSettings,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record CreateRewardRequest
{
    public required string Title { get; init; }
    public required int Cost { get; init; }
    public string? Prompt { get; init; }
    public bool IsUserInputRequired { get; init; }
    public string? BackgroundColor { get; init; }
    public int? MaxPerStream { get; init; }
    public int? MaxPerUserPerStream { get; init; }
    public int? GlobalCooldownSeconds { get; init; }
    public string? ActionType { get; init; }
    public Dictionary<string, object?>? ActionSettings { get; init; }
}

public sealed record UpdateRewardRequest
{
    public string? Title { get; init; }
    public int? Cost { get; init; }
    public string? Prompt { get; init; }
    public bool? IsUserInputRequired { get; init; }
    public bool? IsEnabled { get; init; }
    public bool? IsPaused { get; init; }
    public string? BackgroundColor { get; init; }
    public int? MaxPerStream { get; init; }
    public int? MaxPerUserPerStream { get; init; }
    public int? GlobalCooldownSeconds { get; init; }
    public string? ActionType { get; init; }
    public Dictionary<string, object?>? ActionSettings { get; init; }
}
