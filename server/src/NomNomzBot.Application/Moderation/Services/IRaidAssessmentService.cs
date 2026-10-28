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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>Vets every incoming raider and keeps the result for the moderators.</summary>
public interface IRaidAssessmentService
{
    /// <summary>Assess the raider of <paramref name="raid"/>, store the row and tell the dashboard.</summary>
    Task<Result<RaidAssessmentDto>> AssessAsync(RaidEvent raid, CancellationToken ct = default);

    /// <summary>The channel's assessed raids, newest first.</summary>
    Task<Result<PagedList<RaidAssessmentDto>>> ListRecentAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
