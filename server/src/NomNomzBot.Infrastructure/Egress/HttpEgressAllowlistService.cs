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
using NomNomzBot.Application.Contracts.Egress;
using NomNomzBot.Application.DTOs.Egress;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Egress;

/// <summary>
/// Egress allowlist management. Uniqueness of a host per channel is enforced here (the table index is not
/// unique). The per-row clamps (methods, path prefix, body, size) are not read by any consumer yet, so they are
/// neither exposed nor edited here.
/// </summary>
public sealed class HttpEgressAllowlistService(IApplicationDbContext db, TimeProvider clock)
    : IHttpEgressAllowlistService
{
    /// <summary>Response-size cap stored on a new row (matches the custom data fetcher's 64 KiB cap).</summary>
    private const int DefaultMaxResponseBytes = 64 * 1024;

    public async Task<Result<PagedList<HttpEgressAllowlistDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<HttpEgressAllowlist> query = db.HttpEgressAllowlists.Where(a =>
            a.BroadcasterId == broadcasterId && a.DeletedAt == null
        );
        int total = await query.CountAsync(ct);
        List<HttpEgressAllowlist> rows = await query
            .OrderBy(a => a.Fqdn)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<HttpEgressAllowlistDto>(
                [.. rows.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<HttpEgressAllowlistDto>> CreateAsync(
        Guid broadcasterId,
        Guid actorUserId,
        CreateHttpEgressAllowlistRequest request,
        CancellationToken ct = default
    )
    {
        if (!EgressHostValidator.TryNormalize(request.Host, out string host, out string error))
            return Result.Failure<HttpEgressAllowlistDto>(error, "VALIDATION_FAILED");

        bool exists = await db.HttpEgressAllowlists.AnyAsync(
            a => a.BroadcasterId == broadcasterId && a.Fqdn == host && a.DeletedAt == null,
            ct
        );
        if (exists)
            return Result.Failure<HttpEgressAllowlistDto>(
                "This host is already on the allowlist.",
                "ALREADY_EXISTS"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        HttpEgressAllowlist row = new()
        {
            BroadcasterId = broadcasterId,
            Fqdn = host,
            IsEnabled = true,
            ApprovedByUserId = actorUserId,
            MaxResponseBytes = DefaultMaxResponseBytes,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.HttpEgressAllowlists.Add(row);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(row));
    }

    public async Task<Result<HttpEgressAllowlistDto>> SetEnabledAsync(
        Guid broadcasterId,
        Guid allowlistId,
        bool isEnabled,
        CancellationToken ct = default
    )
    {
        HttpEgressAllowlist? row = await db.HttpEgressAllowlists.FirstOrDefaultAsync(
            a => a.Id == allowlistId && a.BroadcasterId == broadcasterId && a.DeletedAt == null,
            ct
        );
        if (row is null)
            return Result.Failure<HttpEgressAllowlistDto>("Host not found.", "NOT_FOUND");

        row.IsEnabled = isEnabled;
        row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(row));
    }

    private static HttpEgressAllowlistDto ToDto(HttpEgressAllowlist row) =>
        new(row.Id, row.Fqdn, row.IsEnabled, row.ApprovedByUserId, row.CreatedAt, row.UpdatedAt);
}
