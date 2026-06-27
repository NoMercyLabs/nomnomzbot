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
    string? TemplateResponse,
    List<string>? TemplateResponses,
    Guid? PipelineId,
    int CooldownSeconds,
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
    int CooldownSeconds,
    string? Description,
    List<string> Aliases,
    long UseCount,
    DateTime CreatedAt,
    string? TemplateResponse,
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

    [MaxLength(2000)]
    public string? TemplateResponse { get; init; }

    public List<string>? TemplateResponses { get; init; }

    public Guid? PipelineId { get; init; }

    [Range(0, 86400)]
    public int CooldownSeconds { get; init; }

    public bool CooldownPerUser { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }

    public List<string>? Aliases { get; init; }
}

/// <summary>Request to update an existing command.</summary>
public sealed record UpdateCommandDto
{
    public string? Tier { get; init; }
    public int? MinPermissionLevel { get; init; }

    [MaxLength(2000)]
    public string? TemplateResponse { get; init; }

    public List<string>? TemplateResponses { get; init; }

    public Guid? PipelineId { get; init; }

    [Range(0, 86400)]
    public int? CooldownSeconds { get; init; }

    public bool? CooldownPerUser { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }

    public List<string>? Aliases { get; init; }
    public bool? IsEnabled { get; init; }
}
