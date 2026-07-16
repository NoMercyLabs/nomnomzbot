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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Supporters.Dtos;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Domain.Supporters.Entities;

namespace NomNomzBot.Infrastructure.Supporters;

/// <summary>
/// Manages a broadcaster's supporter connections + browses recorded events (supporter-events.md §5). A
/// connection is the enforced enable-toggle for a provider: ingest is default-deny and only fires when a live
/// connection exists (checked in <see cref="SupporterIngestService"/>). Webhook providers verify + ingest
/// through the shared inbound-webhook plane, so the verification secret lives on that endpoint — never here;
/// a socket/ws/poll provider's key (or, for DonorDrive, its public donations URL) is AEAD-sealed onto the
/// connection (supporter-events.md §0 D6) for the ingress runners to open.
/// </summary>
public sealed class SupporterConnectionService : ISupporterConnectionService
{
    private const int MaxPageSize = 100;

    private readonly IApplicationDbContext _db;
    private readonly IReadOnlyDictionary<string, ISupporterSource> _sources;
    private readonly ITokenProtector _protector;

    public SupporterConnectionService(
        IApplicationDbContext db,
        IEnumerable<ISupporterSource> sources,
        ITokenProtector protector
    )
    {
        _db = db;
        _sources = sources.ToDictionary(s => s.SourceKey, StringComparer.OrdinalIgnoreCase);
        _protector = protector;
    }

    /// <summary>The per-connection AEAD context: subject = the tenant, provider = the source, one field role.</summary>
    internal static TokenProtectionContext SecretContext(Guid broadcasterId, string sourceKey) =>
        new(broadcasterId.ToString(), sourceKey, "auth_secret");

    public async Task<Result<IReadOnlyList<SupporterConnectionDto>>> ListAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<SupporterConnectionDto> items = await _db
            .SupporterConnections.Where(c => c.BroadcasterId == broadcasterId)
            .OrderBy(c => c.SourceKey)
            .Select(c => new SupporterConnectionDto(
                c.SourceKey,
                c.ConnectionMode,
                c.AuthSecretCipher != null,
                c.IsEnabled,
                c.Status,
                c.LastEventAt
            ))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<SupporterConnectionDto>>(items);
    }

    public async Task<Result<SupporterConnectionDto>> UpsertAsync(
        Guid broadcasterId,
        Guid actorUserId,
        UpsertSupporterConnectionRequest request,
        CancellationToken ct = default
    )
    {
        string sourceKey = request.SourceKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!_sources.TryGetValue(sourceKey, out ISupporterSource? source))
            return Result.Failure<SupporterConnectionDto>(
                $"Unknown supporter source '{request.SourceKey}'.",
                "VALIDATION_FAILED"
            );

        string mode = request.ConnectionMode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!string.Equals(mode, source.Capabilities.ConnectionMode, StringComparison.Ordinal))
            return Result.Failure<SupporterConnectionDto>(
                $"'{sourceKey}' ingests via '{source.Capabilities.ConnectionMode}', not '{mode}'.",
                "VALIDATION_FAILED"
            );

        // Webhook providers verify at the inbound-webhook endpoint, which owns the secret. Accepting one here
        // would store a secret nothing reads — reject it rather than ship a phantom control.
        if (mode == "webhook" && !string.IsNullOrWhiteSpace(request.AuthSecret))
            return Result.Failure<SupporterConnectionDto>(
                "A webhook provider's verification secret is set on its inbound webhook endpoint, not on the connection.",
                "VALIDATION_FAILED"
            );

        // Revive a soft-deleted row rather than orphaning it (the unique index is filtered on DeletedAt IS NULL).
        SupporterConnection? connection = await _db
            .SupporterConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.BroadcasterId == broadcasterId && c.SourceKey == sourceKey,
                ct
            );

        if (connection is null)
        {
            connection = new SupporterConnection
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                SourceKey = sourceKey,
            };
            _db.SupporterConnections.Add(connection);
        }

        connection.DeletedAt = null;
        connection.ConnectionMode = mode;
        connection.IntegrationConnectionId = request.IntegrationConnectionId;
        connection.IsEnabled = request.IsEnabled;
        if (string.IsNullOrEmpty(connection.Status))
            connection.Status = "idle";

        // A socket/ws/poll provider's key is AEAD-sealed here for the ingress runner to open. An omitted
        // secret on a re-upsert keeps the stored one (toggling IsEnabled must never wipe the credential).
        if (!string.IsNullOrWhiteSpace(request.AuthSecret))
            connection.AuthSecretCipher = await _protector.ProtectAsync(
                request.AuthSecret,
                SecretContext(broadcasterId, sourceKey),
                ct
            );

        await _db.SaveChangesAsync(ct);

        return Result.Success(
            new SupporterConnectionDto(
                connection.SourceKey,
                connection.ConnectionMode,
                connection.AuthSecretCipher != null,
                connection.IsEnabled,
                connection.Status,
                connection.LastEventAt
            )
        );
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid actorUserId,
        string sourceKey,
        CancellationToken ct = default
    )
    {
        string key = sourceKey?.Trim().ToLowerInvariant() ?? string.Empty;
        SupporterConnection? connection = await _db.SupporterConnections.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.SourceKey == key,
            ct
        );
        if (connection is null)
            return Result.Failure($"No '{sourceKey}' supporter connection.", "NOT_FOUND");

        _db.SupporterConnections.Remove(connection); // Soft delete via the interceptor.
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PagedList<SupporterEventDto>>> ListEventsAsync(
        Guid broadcasterId,
        SupporterEventQuery query,
        CancellationToken ct = default
    )
    {
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize is < 1 or > MaxPageSize ? 25 : query.PageSize;

        IQueryable<SupporterEvent> q = _db.SupporterEvents.Where(e =>
            e.BroadcasterId == broadcasterId
        );
        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            string kind = query.Kind.Trim().ToLowerInvariant();
            q = q.Where(e => e.Kind == kind);
        }
        if (!string.IsNullOrWhiteSpace(query.SourceKey))
        {
            string src = query.SourceKey.Trim().ToLowerInvariant();
            q = q.Where(e => e.SourceKey == src);
        }

        int total = await q.CountAsync(ct);
        List<SupporterEventDto> items = await q.OrderByDescending(e => e.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new SupporterEventDto(
                e.Id,
                e.SourceKey,
                e.Kind,
                e.SupporterDisplayName,
                e.AmountMinor,
                e.Currency,
                e.Tier,
                e.Quantity,
                e.MessageText,
                e.IsRecurring,
                e.ReceivedAt
            ))
            .ToListAsync(ct);

        return Result.Success(new PagedList<SupporterEventDto>(items, page, pageSize, total));
    }
}
