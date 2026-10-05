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
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Egress;
using NomNomzBot.Application.DTOs.Egress;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Webhooks.Entities;

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

    public async Task<Result<BlastRadiusDto>> GetDeleteBlastRadiusAsync(
        Guid broadcasterId,
        Guid allowlistId,
        CancellationToken ct = default
    )
    {
        HttpEgressAllowlist? row = await FindAsync(broadcasterId, allowlistId, ct);
        if (row is null)
            return Result.Failure<BlastRadiusDto>("Host not found.", "NOT_FOUND");

        List<string> endpointNames = await OutboundUsing(row)
            .OrderBy(e => e.Name)
            .Select(e => e.Name)
            .ToListAsync(ct);
        List<string> sourceNames = await SourcesUsingAsync(row, ct);

        List<BlastRadiusCategoryDto> categories = [];
        if (endpointNames.Count > 0)
            categories.Add(
                new BlastRadiusCategoryDto(
                    BlastRadiusCategoryKeys.OutboundWebhookEndpoints,
                    endpointNames.Count,
                    [.. endpointNames.Take(5)]
                )
            );
        if (sourceNames.Count > 0)
            categories.Add(
                new BlastRadiusCategoryDto(
                    BlastRadiusCategoryKeys.CustomDataSources,
                    sourceNames.Count,
                    [.. sourceNames.Take(5)]
                )
            );
        return Result.Success(new BlastRadiusDto(categories, IsMinimum: false));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid allowlistId,
        CancellationToken ct = default
    )
    {
        HttpEgressAllowlist? row = await FindAsync(broadcasterId, allowlistId, ct);
        if (row is null)
            return Result.Failure("Host not found.", "NOT_FOUND");

        DateTime now = clock.GetUtcNow().UtcDateTime;
        // The outbound dispatcher does not re-read the allowlist when it sends, so the endpoints that used this
        // host are switched off here, in the same save, or they would keep delivering to a removed host.
        List<OutboundWebhookEndpoint> dependents = await OutboundUsing(row).ToListAsync(ct);
        foreach (OutboundWebhookEndpoint endpoint in dependents)
        {
            endpoint.IsEnabled = false;
            endpoint.DisabledAt = now;
            endpoint.UpdatedAt = now;
        }
        row.DeletedAt = now;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private Task<HttpEgressAllowlist?> FindAsync(
        Guid broadcasterId,
        Guid allowlistId,
        CancellationToken ct
    ) =>
        db.HttpEgressAllowlists.FirstOrDefaultAsync(
            a => a.Id == allowlistId && a.BroadcasterId == broadcasterId && a.DeletedAt == null,
            ct
        );

    /// <summary>Live outbound endpoints of the row's channel that point at it, by link or by host name.</summary>
    private IQueryable<OutboundWebhookEndpoint> OutboundUsing(HttpEgressAllowlist row) =>
        db.OutboundWebhookEndpoints.Where(e =>
            e.BroadcasterId == row.BroadcasterId
            && e.DeletedAt == null
            && (e.HttpEgressAllowlistId == row.Id || e.Fqdn == row.Fqdn)
        );

    /// <summary>
    /// Live custom data sources whose URL host is this row's host. The source keeps only a URL (no link), so the
    /// host is parsed the same way the save-time and fetch-time gates parse it.
    /// </summary>
    private async Task<List<string>> SourcesUsingAsync(
        HttpEgressAllowlist row,
        CancellationToken ct
    )
    {
        var candidates = await db
            .CustomDataSources.Where(s =>
                s.BroadcasterId == row.BroadcasterId && s.DeletedAt == null && s.EndpointUrl != null
            )
            .Select(s => new { s.DisplayName, s.EndpointUrl })
            .ToListAsync(ct);
        return
        [
            .. candidates
                .Where(s =>
                    Uri.TryCreate(s.EndpointUrl, UriKind.Absolute, out Uri? url)
                    && string.Equals(url.Host, row.Fqdn, StringComparison.Ordinal)
                )
                .Select(s => s.DisplayName)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];
    }

    private static HttpEgressAllowlistDto ToDto(HttpEgressAllowlist row) =>
        new(row.Id, row.Fqdn, row.IsEnabled, row.ApprovedByUserId, row.CreatedAt, row.UpdatedAt);
}
