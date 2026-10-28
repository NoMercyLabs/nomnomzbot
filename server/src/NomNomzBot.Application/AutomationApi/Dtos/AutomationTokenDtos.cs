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

namespace NomNomzBot.Application.AutomationApi.Dtos;

/// <summary>An automation API token as the dashboard sees it — the secret never appears here.</summary>
public sealed record AutomationTokenDto(
    Guid Id,
    string Name,
    string TokenPrefix,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<Guid> AllowedPipelineIds,
    IReadOnlyList<AutomationTargetDto> AllowedTargets,
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

    /// <summary>
    /// When set, <c>invoke</c> is also pinned to these targets (commands, pipelines, actions); they add to
    /// <see cref="AllowedPipelineIds"/>. Null/empty on both = any target.
    /// </summary>
    public IReadOnlyList<AutomationTargetDto>? AllowedTargets { get; init; }

    public DateTime? ExpiresAt { get; init; }
}
