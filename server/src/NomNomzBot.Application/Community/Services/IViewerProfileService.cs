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
using NomNomzBot.Application.Community.Dtos;

namespace NomNomzBot.Application.Community.Services;

/// <summary>
/// Assembles the Community Profile page's single-person view (owner punch list 2026-09-08 §3) — every
/// per-channel-per-user data point the domain model tracks, folded from the services/tables that already own
/// each slice rather than duplicating their logic. Read-only; each writer keeps its own dedicated endpoint.
/// </summary>
public interface IViewerProfileService
{
    /// <summary>
    /// The full profile for one person in one channel. NOT_FOUND if the person has no local <c>User</c> row
    /// at all — every other group inside the result degrades to empty/null per-group rather than failing the
    /// whole read, since "no data in this group yet" is a normal, truthful state for a real viewer.
    /// </summary>
    Task<Result<ViewerProfileSummaryDto>> GetProfileAsync(
        Guid broadcasterId,
        Guid userId,
        CancellationToken ct = default
    );
}
