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

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>Full command detail for viewing/editing.</summary>
public sealed record CommandDto(
    Guid Id,
    string Name,
    string Tier,
    int MinPermissionLevel,
    bool IsEnabled,
    string PrefixMode,
    string? CustomPrefix,
    string MatchMode,
    string? MatchPattern,
    string? TemplateResponse,
    List<string>? TemplateResponses,
    Guid? PipelineId,
    int CooldownSeconds,
    int UserCooldownSeconds,
    bool CooldownPerUser,
    string? Description,
    List<string> Aliases,
    long UseCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Lightweight command info for list views — includes the response text and pipeline id so the edit
/// form can pre-fill without a separate detail fetch.</summary>
public sealed record CommandListItem(
    Guid Id,
    string Name,
    string Tier,
    int MinPermissionLevel,
    bool IsEnabled,
    string PrefixMode,
    string? CustomPrefix,
    string MatchMode,
    string? MatchPattern,
    int CooldownSeconds,
    int UserCooldownSeconds,
    bool CooldownPerUser,
    string? Description,
    List<string> Aliases,
    long UseCount,
    DateTime CreatedAt,
    string? TemplateResponse,
    List<string>? TemplateResponses,
    Guid? PipelineId
);

/// <summary>Request to create a new command.</summary>
public sealed record CreateCommandDto
{
    [Required, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>template | pipeline | code</summary>
    public string Tier { get; init; } = "template";

    /// <summary>0=everyone, 1=follower, 2=sub, 3=vip, 4=moderator, 5=broadcaster</summary>
    public int MinPermissionLevel { get; init; }

    /// <summary>How the prefix is resolved: Default (channel prefix) | Custom | None.</summary>
    [MaxLength(20)]
    public string PrefixMode { get; init; } = "Default";

    /// <summary>Custom prefix character(s) when <see cref="PrefixMode"/> is Custom.</summary>
    [MaxLength(8)]
    public string? CustomPrefix { get; init; }

    /// <summary>How the trigger input is matched against the name: StartsWith | Exact | Contains | Regex.</summary>
    [MaxLength(20)]
    public string MatchMode { get; init; } = "StartsWith";

    /// <summary>Required when <see cref="MatchMode"/> is Regex; null otherwise.</summary>
    [MaxLength(200)]
    public string? MatchPattern { get; init; }

    [MaxLength(2000)]
    public string? TemplateResponse { get; init; }

    public List<string>? TemplateResponses { get; init; }

    public Guid? PipelineId { get; init; }

    [Range(0, 86400)]
    public int CooldownSeconds { get; init; }

    /// <summary>Per-user cooldown duration in seconds; applied when <see cref="CooldownPerUser"/> is true.</summary>
    [Range(0, 86400)]
    public int UserCooldownSeconds { get; init; }

    public bool CooldownPerUser { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }

    public List<string>? Aliases { get; init; }

    /// <summary>Whether the command is live on creation (default true).</summary>
    public bool IsEnabled { get; init; } = true;
}

/// <summary>Request to update an existing command.</summary>
public sealed record UpdateCommandDto
{
    public string? Tier { get; init; }
    public int? MinPermissionLevel { get; init; }

    /// <summary>How the prefix is resolved: Default | Custom | None. Null leaves it unchanged.</summary>
    [MaxLength(20)]
    public string? PrefixMode { get; init; }

    /// <summary>Custom prefix when <see cref="PrefixMode"/> is Custom; empty string clears it.</summary>
    [MaxLength(8)]
    public string? CustomPrefix { get; init; }

    /// <summary>How the trigger input is matched: StartsWith | Exact | Contains | Regex. Null leaves it unchanged.</summary>
    [MaxLength(20)]
    public string? MatchMode { get; init; }

    /// <summary>Regex pattern when <see cref="MatchMode"/> is Regex; empty string clears it.</summary>
    [MaxLength(200)]
    public string? MatchPattern { get; init; }

    [MaxLength(2000)]
    public string? TemplateResponse { get; init; }

    public List<string>? TemplateResponses { get; init; }

    public Guid? PipelineId { get; init; }

    [Range(0, 86400)]
    public int? CooldownSeconds { get; init; }

    /// <summary>Per-user cooldown duration in seconds; applied when <see cref="CooldownPerUser"/> is true.</summary>
    [Range(0, 86400)]
    public int? UserCooldownSeconds { get; init; }

    public bool? CooldownPerUser { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }

    public List<string>? Aliases { get; init; }
    public bool? IsEnabled { get; init; }
}
