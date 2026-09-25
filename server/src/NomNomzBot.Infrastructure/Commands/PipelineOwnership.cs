// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// The one check every surface runs before it binds a pipeline id from a request: the pipeline must exist in
/// the SAME channel and not be deleted. Without it a known GUID from another channel binds and runs there.
/// </summary>
public static class PipelineOwnership
{
    public const string ErrorCode = "PIPELINE_NOT_IN_CHANNEL";

    /// <summary>
    /// Succeeds for "no binding" (null or <see cref="Guid.Empty"/>, the clear sentinel) and for a live
    /// pipeline owned by <paramref name="broadcasterId"/>; fails with <see cref="ErrorCode"/> otherwise.
    /// </summary>
    public static async Task<Result> EnsurePipelineInChannelAsync(
        this IApplicationDbContext db,
        Guid broadcasterId,
        Guid? pipelineId,
        CancellationToken ct = default
    )
    {
        if (pipelineId is null || pipelineId == Guid.Empty)
            return Result.Success();
        bool owned = await db
            .Pipelines.IgnoreQueryFilters()
            .AnyAsync(
                p =>
                    p.Id == pipelineId.Value
                    && p.BroadcasterId == broadcasterId
                    && p.DeletedAt == null,
                ct
            );
        return owned
            ? Result.Success()
            : Result.Failure("That pipeline does not exist in this channel.", ErrorCode);
    }
}
