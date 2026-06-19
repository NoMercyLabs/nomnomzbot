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

/// <summary>An event response configuration.</summary>
public sealed record EventResponseDto(
    int Id,
    string EventType,
    bool IsEnabled,
    string ResponseType,
    string? Message,
    string? PipelineJson,
    Dictionary<string, string> Metadata,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Lightweight event response summary.</summary>
public sealed record EventResponseListItem(
    int Id,
    string EventType,
    bool IsEnabled,
    string ResponseType,
    DateTime UpdatedAt
);

/// <summary>Request to update an event response configuration.</summary>
public sealed record UpdateEventResponseDto
{
    public bool? IsEnabled { get; init; }

    [RegularExpression("^(chat_message|overlay|pipeline|none)$")]
    public string? ResponseType { get; init; }

    [MaxLength(2000)]
    public string? Message { get; init; }

    public string? PipelineJson { get; init; }

    public Dictionary<string, string>? Metadata { get; init; }
}
