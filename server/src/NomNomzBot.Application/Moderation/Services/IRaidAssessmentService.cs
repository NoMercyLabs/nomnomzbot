// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
