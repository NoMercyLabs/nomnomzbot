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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;

namespace NomNomzBot.Application.Dashboard.Services;

/// <summary>
/// Aggregates the dashboard render manifest for a channel by composing the existing access, feature,
/// integration, and scope surfaces into one response.
/// </summary>
public interface IRenderManifestService
{
    /// <summary>
    /// Build the render manifest for <paramref name="broadcasterId"/> as seen by
    /// <paramref name="userId"/>. Access is always resolved and load-bearing; the feature,
    /// integration, and scope sections are populated only where the caller clears the surface's read
    /// floor, and of those, integrations and scopes degrade to an empty section on failure.
    /// </summary>
    Task<Result<RenderManifestDto>> GetManifestAsync(
        Guid userId,
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
