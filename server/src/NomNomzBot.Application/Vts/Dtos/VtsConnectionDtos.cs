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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Vts.Entities;

namespace NomNomzBot.Application.Vts.Dtos;

/// <summary>
/// A channel's VTS connection as the dashboard sees it (vtube-studio.md §3). The plugin token never
/// appears — only <see cref="HasPluginToken"/>.
/// </summary>
public sealed record VtsConnectionDto(
    string Mode,
    string Endpoint,
    bool HasPluginToken,
    bool HasBridgeToken,
    int EventSubscriptionsMask,
    bool IsEnabled,
    string Status,
    DateTime? LastConnectedAt,
    IReadOnlyList<EventKindDto> EventKinds
);

/// <summary>Upsert request — the plugin token is never written here; the authorize flow mints it.</summary>
public sealed record UpsertVtsConnectionRequest
{
    /// <summary>Transport plane: <c>direct</c> | <c>bridge</c>.</summary>
    [Required]
    [RegularExpression("^(direct|bridge)$")]
    public string Mode { get; init; } = null!;

    [MaxLength(200)]
    [RegularExpression("^wss?://[^\\s]+$")]
    public string? Endpoint { get; init; }

    [Range(0, VtsConnectionDefaults.MaxEventSubscriptionsMask)]
    public int? EventSubscriptionsMask { get; init; }

    public bool IsEnabled { get; init; }
}
