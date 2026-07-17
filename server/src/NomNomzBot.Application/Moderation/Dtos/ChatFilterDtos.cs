// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Moderation.Enums;

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>A custom chat filter (moderation.md J.6) as surfaced to the dashboard.</summary>
public sealed record ChatFilterDto(
    Guid Id,
    ChatFilterType FilterType,
    string Name,
    string? Pattern,
    IReadOnlyList<string> Terms,
    ChatFilterAction Action,
    int? TimeoutSeconds,
    int ExemptMinRoleLevel,
    bool IsEnabled,
    bool IsCaseSensitive,
    long MatchCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Creates a chat filter. A regex filter validates that <see cref="Pattern"/> compiles.</summary>
public sealed record CreateChatFilterRequest
{
    public required ChatFilterType FilterType { get; init; }
    public required string Name { get; init; }
    public required ChatFilterAction Action { get; init; }
    public string? Pattern { get; init; }
    public List<string>? Terms { get; init; }
    public string? LinkPolicyJson { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int ExemptMinRoleLevel { get; init; } = 10; // moderator floor
    public bool IsEnabled { get; init; } = true;
    public bool IsCaseSensitive { get; init; }
}

/// <summary>Patches an existing chat filter — only the supplied fields change.</summary>
public sealed record UpdateChatFilterRequest
{
    public string? Name { get; init; }
    public ChatFilterAction? Action { get; init; }
    public string? Pattern { get; init; }
    public List<string>? Terms { get; init; }
    public string? LinkPolicyJson { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? ExemptMinRoleLevel { get; init; }
    public bool? IsEnabled { get; init; }
    public bool? IsCaseSensitive { get; init; }
}
