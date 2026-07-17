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
    DateTime? LastConnectedAt
);

/// <summary>Upsert request — the plugin token is never written here; the authorize flow mints it.</summary>
public sealed record UpsertVtsConnectionRequest
{
    /// <summary>Transport plane: <c>direct</c> | <c>bridge</c>.</summary>
    [Required]
    [RegularExpression("^(direct|bridge)$")]
    public string Mode { get; init; } = null!;

    [MaxLength(200)]
    public string? Endpoint { get; init; }

    public int? EventSubscriptionsMask { get; init; }

    public bool IsEnabled { get; init; }
}
