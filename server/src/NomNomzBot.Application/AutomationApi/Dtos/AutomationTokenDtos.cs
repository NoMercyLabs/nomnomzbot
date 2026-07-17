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

namespace NomNomzBot.Application.AutomationApi.Dtos;

/// <summary>An automation API token as the dashboard sees it — the secret never appears here.</summary>
public sealed record AutomationTokenDto(
    Guid Id,
    string Name,
    string TokenPrefix,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<Guid> AllowedPipelineIds,
    DateTime? LastUsedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    DateTime CreatedAt
);

/// <summary>
/// The one-time issue result: <see cref="Secret"/> is shown exactly once (create/rotate) and is never
/// retrievable again — only its hash is stored.
/// </summary>
public sealed record IssuedAutomationTokenDto(string Secret, AutomationTokenDto Token);

/// <summary>Request to create an automation API token.</summary>
public sealed record CreateAutomationTokenRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; init; } = null!;

    /// <summary>Data-plane scopes ⊆ <c>invoke | read | events | chat</c>; at least one required.</summary>
    [Required]
    [MinLength(1)]
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>When set, <c>invoke</c> is pinned to exactly these pipelines; null/empty = any.</summary>
    public IReadOnlyList<Guid>? AllowedPipelineIds { get; init; }

    public DateTime? ExpiresAt { get; init; }
}
