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
    int Id,
    string Name,
    string Type,
    string Permission,
    bool IsEnabled,
    string? Response,
    List<string>? Responses,
    object? Pipeline,
    int CooldownSeconds,
    bool CooldownPerUser,
    string? Description,
    List<string> Aliases,
    int UsageCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Lightweight command info for list views.</summary>
public sealed record CommandListItem(
    int Id,
    string Name,
    string Type,
    string Permission,
    bool IsEnabled,
    int CooldownSeconds,
    string? Description,
    List<string> Aliases,
    int UsageCount,
    DateTime CreatedAt
);

/// <summary>Request to create a new command.</summary>
public sealed record CreateCommandDto
{
    [Required, MaxLength(100)]
    public required string Name { get; init; }

    [Required]
    public string Type { get; init; } = "text";

    public string Permission { get; init; } = "everyone";

    [MaxLength(2000)]
    public string? Response { get; init; }

    public List<string>? Responses { get; init; }

    public object? Pipeline { get; init; }

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
    public string? Type { get; init; }
    public string? Permission { get; init; }

    [MaxLength(2000)]
    public string? Response { get; init; }

    public List<string>? Responses { get; init; }
    public object? Pipeline { get; init; }

    [Range(0, 86400)]
    public int? CooldownSeconds { get; init; }

    public bool? CooldownPerUser { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }

    public List<string>? Aliases { get; init; }
    public bool? IsEnabled { get; init; }
}
