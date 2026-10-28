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

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>Full timer detail for viewing/editing.</summary>
public sealed record TimerDto(
    Guid Id,
    string Name,
    List<string> Messages,
    int IntervalMinutes,
    int MinChatActivity,
    bool IsEnabled,
    bool FireOnce,
    Guid? PipelineId,
    DateTime? LastFiredAt,
    int NextMessageIndex,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public bool SpeakWithTts { get; init; }

    /// <summary>Where a message timer's line goes and whether the overlay line is HTML; null = the platform chat as plain text.</summary>
    public ReplyDelivery? ReplyDelivery { get; init; }

    /// <summary>What a firing runs when bound directly: a command, an action or a pipeline. Null for a plain message timer.</summary>
    public BindingTarget? Target { get; init; }
}

/// <summary>Lightweight timer info for list views.</summary>
public sealed record TimerListItem(
    Guid Id,
    string Name,
    int IntervalMinutes,
    bool IsEnabled,
    bool FireOnce,
    DateTime? LastFiredAt,
    int MessageCount,
    DateTime CreatedAt
)
{
    /// <summary>True when the timer was installed from a platform template, so it can be restored to it.</summary>
    public bool HasPlatformDefault { get; init; }
}

/// <summary>Request to create a new timer.</summary>
public sealed record CreateTimerDto
{
    [Required, MaxLength(100)]
    public required string Name { get; init; }

    public required List<string> Messages { get; init; } = [];

    public Guid? PipelineId { get; init; }

    [Range(1, 1440)]
    public int IntervalMinutes { get; init; } = 30;

    [Range(0, 10000)]
    public int MinChatActivity { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>When true the timer fires once and then disables itself, instead of looping on the interval.</summary>
    public bool FireOnce { get; init; }

    public bool SpeakWithTts { get; init; }

    /// <summary>Where a message timer's line goes and whether the overlay line is HTML; null = the platform chat as plain text.</summary>
    public ReplyDelivery? ReplyDelivery { get; init; }

    /// <summary>
    /// What a firing runs: a command (by id), a pipeline or an action. Wins over <see cref="PipelineId"/>
    /// when both are sent. The command must exist in the channel.
    /// </summary>
    public BindingTarget? Target { get; init; }
}

/// <summary>Request to update an existing timer.</summary>
public sealed record UpdateTimerDto
{
    [MaxLength(100)]
    public string? Name { get; init; }

    public List<string>? Messages { get; init; }

    public Guid? PipelineId { get; init; }

    [Range(1, 1440)]
    public int? IntervalMinutes { get; init; }

    [Range(0, 10000)]
    public int? MinChatActivity { get; init; }

    public bool? IsEnabled { get; init; }

    /// <summary>When set, toggles one-shot mode (fire once then disable) vs looping.</summary>
    public bool? FireOnce { get; init; }

    public bool? SpeakWithTts { get; init; }

    /// <summary>Where a message timer's line goes and whether the overlay line is HTML. Absent leaves it unchanged.</summary>
    public ReplyDelivery? ReplyDelivery { get; init; }

    /// <summary>
    /// The new target; absent leaves the binding unchanged. A pipeline target with no id clears the binding.
    /// Wins over <see cref="PipelineId"/> when both are sent. A command target must exist in the channel.
    /// </summary>
    public BindingTarget? Target { get; init; }
}
