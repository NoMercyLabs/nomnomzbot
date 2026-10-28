// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.Rewards.Dtos;

/// <summary>One live/recent countdown for a time-limited reward redemption. <c>RemainingSeconds</c> is
/// the CURRENT clock-derived value; <c>Status</c> is running | paused | completed | canceled.</summary>
public sealed record RedemptionTimerDto(
    Guid Id,
    string RedemptionId,
    string RewardId,
    string RewardTitle,
    string RedeemedBy,
    int DurationSeconds,
    int RemainingSeconds,
    string Status,
    DateTime StartedAt
);

/// <summary>One entry in the channel-points redemption queue (rewards.md) — a redemption with its current
/// status (<c>unfulfilled</c> / <c>fulfilled</c> / <c>canceled</c>), folded from the journal.</summary>
public sealed record RedemptionListItem(
    string RedemptionId,
    string RewardId,
    string RewardTitle,
    string UserId,
    string UserDisplayName,
    string? UserAvatarUrl,
    int Cost,
    string? UserInput,
    string Status,
    DateTime RedeemedAt,
    // The redeemer's Twitch chat color, or null when never seen in chat.
    string? UserColor = null
);

/// <summary>Whether the channel can have channel points: Twitch gives them to Affiliates and Partners only.</summary>
public sealed record RewardAvailability(bool ChannelPointsAvailable, string BroadcasterType);

public sealed record RewardDetail(
    string Id,
    string Title,
    string? Prompt,
    // The chat template announced when the reward is redeemed; null when none.
    string? Response,
    int Cost,
    bool IsEnabled,
    // True when the bot created this reward under its own Twitch client and may edit/delete it; false for
    // externally-created rewards (Twitch UI / other apps) the dashboard must show as read-only.
    bool IsManageable,
    bool IsUserInputRequired,
    bool IsPaused,
    // False while the row has no Twitch reward id: it is saved here but does not exist at Twitch.
    bool IsOnTwitch,
    // Set once "take control" hit Twitch's title-uniqueness conflict and is waiting on the operator to
    // rename/delete the external reward on Twitch; the dashboard swaps the action to "Finalize migration".
    bool IsMigrationPending,
    string? BackgroundColor,
    string? ImageUrl,
    int? MaxPerStream,
    int? MaxPerUserPerStream,
    int? GlobalCooldownSeconds,
    // Opt-in countdown for time-limited rewards: redeeming starts a RedemptionTimer for this many
    // seconds; completion fulfills the redemption. Null = a normal reward.
    int? TimerDurationSeconds,
    // Optional saved-pipeline binding: redeeming this reward dispatches that pipeline (e.g. a play_sound
    // step). Null = no bound pipeline.
    Guid? PipelineId,
    // What a redemption runs, chosen directly: a saved pipeline, a command or an action (no wrapper
    // pipeline). Null when nothing is bound.
    BindingTarget? Target,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // True when a redemption with viewer text speaks that text through TTS in the viewer's voice.
    bool SpeaksViaTts = false,
    // Where the Response line goes and whether the overlay line is HTML; null = the platform chat as plain text.
    ReplyDelivery? ReplyDelivery = null
);

public sealed record CreateRewardRequest
{
    public required string Title { get; init; }
    public required int Cost { get; init; }
    public string? Prompt { get; init; }

    /// <summary>The chat template announced when the reward is redeemed; null when none.</summary>
    public string? Response { get; init; }

    /// <summary>Where the Response line goes and whether the overlay line is HTML; null = the platform chat as plain text.</summary>
    public ReplyDelivery? ReplyDelivery { get; init; }

    public bool IsUserInputRequired { get; init; }
    public string? BackgroundColor { get; init; }
    public int? MaxPerStream { get; init; }
    public int? MaxPerUserPerStream { get; init; }
    public int? GlobalCooldownSeconds { get; init; }

    /// <summary>Opt-in countdown (seconds, capped at 24h) — makes this a time-limited reward.</summary>
    public int? TimerDurationSeconds { get; init; }

    /// <summary>Speak the viewer's redemption text through TTS in their voice.</summary>
    public bool SpeaksViaTts { get; init; }

    /// <summary>Optional saved-pipeline binding — redeeming this reward runs that pipeline.</summary>
    public Guid? PipelineId { get; init; }

    /// <summary>
    /// What a redemption runs: a command (by id), a pipeline or an action. Wins over <see cref="PipelineId"/>
    /// when both are sent. The command must exist in the channel.
    /// </summary>
    public BindingTarget? Target { get; init; }
}

public sealed record UpdateRewardRequest
{
    public string? Title { get; init; }
    public int? Cost { get; init; }
    public string? Prompt { get; init; }

    /// <summary>The chat template announced when the reward is redeemed; null leaves it unchanged, an
    /// empty string clears it.</summary>
    public string? Response { get; init; }

    /// <summary>Where the Response line goes and whether the overlay line is HTML. Absent leaves it unchanged; send target chat to reset it.</summary>
    public ReplyDelivery? ReplyDelivery { get; init; }

    public bool? IsUserInputRequired { get; init; }
    public bool? IsEnabled { get; init; }
    public bool? IsPaused { get; init; }
    public string? BackgroundColor { get; init; }

    // The three redemption limits: absent (null) leaves the limit as it is, 0 turns it off, a positive value
    // sets it. (The full-replace PUT on the controller reads an absent limit as off.)
    public int? MaxPerStream { get; init; }
    public int? MaxPerUserPerStream { get; init; }
    public int? GlobalCooldownSeconds { get; init; }

    /// <summary>Countdown seconds for a time-limited reward; 0 clears it, absent leaves it unchanged.</summary>
    public int? TimerDurationSeconds { get; init; }

    /// <summary>Speak the viewer's redemption text through TTS; absent leaves it unchanged.</summary>
    public bool? SpeaksViaTts { get; init; }

    /// <summary>Saved-pipeline binding; <see cref="Guid.Empty"/> clears it, absent leaves it unchanged.</summary>
    public Guid? PipelineId { get; init; }

    /// <summary>
    /// The new target; absent leaves the binding unchanged. A pipeline target with no id clears the binding.
    /// Wins over <see cref="PipelineId"/> when both are sent. A command target must exist in the channel.
    /// </summary>
    public BindingTarget? Target { get; init; }
}
