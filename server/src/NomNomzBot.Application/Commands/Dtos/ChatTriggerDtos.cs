// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>One keyword auto-reply: fires on ordinary chat lines matching the pattern.</summary>
public sealed record ChatTriggerDto(
    Guid Id,
    string Pattern,
    string MatchType,
    bool CaseSensitive,
    bool IsEnabled,
    string? Response,
    Guid? PipelineId,
    int CooldownSeconds,
    string MinPermissionLevel,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record CreateChatTriggerRequest
{
    [Required]
    [MaxLength(200)]
    public required string Pattern { get; init; }

    /// <summary>contains | exact | starts_with | regex (default contains).</summary>
    [RegularExpression("^(contains|exact|starts_with|regex)$")]
    public string MatchType { get; init; } = "contains";

    public bool CaseSensitive { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Template response — required unless a pipeline is bound.</summary>
    [MaxLength(500)]
    public string? Response { get; init; }

    /// <summary>Bound pipeline for chained reactions; wins over the template response.</summary>
    public Guid? PipelineId { get; init; }

    /// <summary>Channel-wide cooldown between fires (spam guard, default 30s, capped 24h).</summary>
    public int CooldownSeconds { get; init; } = 30;

    /// <summary>
    /// The lowest rung whose speaker may fire this trigger, by NAME —
    /// <c>Everyone | Subscriber | Vip | Artist | Moderator | LeadModerator | Editor | Broadcaster</c>.
    /// An unrecognised name is refused rather than clamped: a typo must not quietly widen who can fire it.
    /// </summary>
    [MaxLength(20)]
    public string MinPermissionLevel { get; init; } = nameof(PermissionLevel.Everyone);
}

public sealed record UpdateChatTriggerRequest
{
    [MaxLength(200)]
    public string? Pattern { get; init; }

    [RegularExpression("^(contains|exact|starts_with|regex)$")]
    public string? MatchType { get; init; }

    public bool? CaseSensitive { get; init; }
    public bool? IsEnabled { get; init; }

    [MaxLength(500)]
    public string? Response { get; init; }

    public Guid? PipelineId { get; init; }
    public int? CooldownSeconds { get; init; }

    /// <summary>The rung name (see <see cref="CreateChatTriggerRequest.MinPermissionLevel"/>); null leaves it.</summary>
    [MaxLength(20)]
    public string? MinPermissionLevel { get; init; }
}
