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

/// <summary>Full pipeline details including the deserialized node graph.</summary>
public sealed record PipelineDto(
    int Id,
    string ChannelId,
    string Name,
    string? Description,
    bool IsEnabled,
    object Graph,
    int TriggerCount,
    DateTime? LastTriggeredAt,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Lightweight pipeline summary for list views.</summary>
public sealed record PipelineListItemDto(
    int Id,
    string Name,
    string? Description,
    bool IsEnabled,
    int TriggerCount,
    DateTime? LastTriggeredAt,
    DateTime UpdatedAt
);

/// <summary>Request to create a new pipeline.</summary>
public sealed record CreatePipelineDto
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = null!;

    [MaxLength(500)]
    public string? Description { get; init; }

    public bool IsEnabled { get; init; } = true;

    public object? Graph { get; init; }
}

/// <summary>Request to update an existing pipeline.</summary>
public sealed record UpdatePipelineDto
{
    [MaxLength(200)]
    public string? Name { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }

    public bool? IsEnabled { get; init; }

    public object? Graph { get; init; }
}
